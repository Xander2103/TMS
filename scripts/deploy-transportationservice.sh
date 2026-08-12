#!/usr/bin/env bash
#
# deploy-transportationservice.sh — production deployment for TransportationService.
#
# Source of truth lives in the repo (scripts/deploy-transportationservice.sh);
# install/update on the server with:
#   sudo install -m 0755 scripts/deploy-transportationservice.sh /usr/local/bin/deploy-transportationservice.sh
#
# Hard guarantees (born from the 2026-08 incident where new code hit an
# unmigrated database):
#   * EF Core migrations are applied BEFORE the API is restarted — never after.
#   * A failed migration stops the deployment; the previously published release
#     keeps running untouched and the backup path is printed.
#   * The EF run sees the SAME environment as systemd (/etc/transportationservice/tms.env).
#   * Application-table ownership is verified BEFORE migrating: every table in
#     schema public must be owned by the migration DB user. Ownership is never
#     changed by this script — that is a deliberate admin action.
#   * No secret (connection string, passwords, keys) is ever echoed or logged.
#   * No command failure is masked through pipes/tail (set -Eeuo pipefail, no
#     status-swallowing pipelines around gating commands).
#
# Usage:  sudo /usr/local/bin/deploy-transportationservice.sh [git-ref]
#         git-ref defaults to DEPLOY_BRANCH below (branch name or commit sha).

set -Eeuo pipefail
IFS=$'\n\t'
umask 027

# --- Configuration (paths only — never credentials) --------------------------
REPO_DIR="${REPO_DIR:-/opt/transportationservice/src}"
DEPLOY_BRANCH="${1:-${DEPLOY_BRANCH:-nav-redesign}}"
SERVICE_NAME="transportationservice-api"
ENV_FILE="/etc/transportationservice/tms.env"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/transportationservice}"
RELEASES_DIR="${RELEASES_DIR:-/opt/transportationservice/releases}"
CURRENT_LINK="${CURRENT_LINK:-/opt/transportationservice/current}"
WEB_ROOT="${WEB_ROOT:-/var/www/transportationservice}"
API_PROJECT="TransportationService.Api"
WEB_PROJECT="TransportationService.Web"
STARTUP_WAIT_SECONDS="${STARTUP_WAIT_SECONDS:-25}"

# --- Logging helpers ---------------------------------------------------------
CURRENT_STEP="init"
BACKUP_PATH="(geen back-up gemaakt)"
MIGRATION_RESULT="niet uitgevoerd"

ts() { date '+%Y-%m-%d %H:%M:%S'; }
step() { CURRENT_STEP="$1"; printf '\n[%s] ==> %s\n' "$(ts)" "$1"; }
note() { printf '[%s]     %s\n' "$(ts)" "$1"; }

on_error() {
    local exit_code=$?
    printf '\n[%s] !! DEPLOYMENT MISLUKT in stap: %s (exitcode %d)\n' "$(ts)" "$CURRENT_STEP" "$exit_code" >&2
    printf '[%s] !! De draaiende service is NIET herstart op incompatibele code.\n' "$(ts)" >&2
    printf '[%s] !! Database-back-up: %s\n' "$(ts)" "$BACKUP_PATH" >&2
    printf '[%s] !! Migratieresultaat: %s\n' "$(ts)" "$MIGRATION_RESULT" >&2
    printf '[%s] !! Vorige release blijft actief onder: %s\n' "$(ts)" "$CURRENT_LINK" >&2
    exit "$exit_code"
}
trap on_error ERR

require_cmd() {
    command -v "$1" >/dev/null 2>&1 || { note "Vereist commando ontbreekt: $1"; return 1; }
}

# --- Step 0: preconditions ---------------------------------------------------
step "Voorbereiding: vereisten controleren"
require_cmd git
require_cmd dotnet
require_cmd npm
require_cmd psql
require_cmd pg_dump
require_cmd curl
require_cmd nginx
require_cmd rsync
require_cmd systemctl
dotnet ef --version >/dev/null 2>&1 || {
    note "dotnet-ef ontbreekt. Installeer eenmalig: dotnet tool install --global dotnet-ef"
    exit 1
}
[[ -d "$REPO_DIR" ]] || { note "Repo-map ontbreekt: $REPO_DIR"; exit 1; }
[[ -r "$ENV_FILE" ]] || { note "Omgevingsbestand niet leesbaar: $ENV_FILE (root vereist)"; exit 1; }
mkdir -p "$BACKUP_DIR" "$RELEASES_DIR"
cd "$REPO_DIR"

# --- Environment: load the EXACT file systemd uses. Values are exported for
# --- child processes (pg_dump, dotnet ef, the API build) but NEVER printed. ---
step "Productieomgeving laden ($ENV_FILE) — waarden worden nooit getoond"
while IFS= read -r line; do
    # systemd EnvironmentFile format: KEY=VALUE, '#' comments, optional quotes.
    [[ -z "$line" || "$line" =~ ^[[:space:]]*# ]] && continue
    [[ "$line" == *=* ]] || continue
    key="${line%%=*}"
    value="${line#*=}"
    # Strip one pair of surrounding quotes, as systemd does.
    if [[ "$value" == \"*\" && "$value" == *\" ]]; then value="${value#\"}"; value="${value%\"}"; fi
    if [[ "$value" == \'*\' && "$value" == *\' ]]; then value="${value#\'}"; value="${value%\'}"; fi
    export "$key=$value"
done < "$ENV_FILE"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
note "Omgeving geladen (ASPNETCORE_ENVIRONMENT=$ASPNETCORE_ENVIRONMENT)."

# --- Parse DB coordinates from the connection string (in memory only). -------
# Npgsql format: Host=..;Port=..;Database=..;Username=..;Password=..
CONN="${ConnectionStrings__DefaultConnection:-}"
[[ -n "$CONN" ]] || { note "ConnectionStrings__DefaultConnection ontbreekt in $ENV_FILE"; exit 1; }
conn_part() {
    local name="$1" part
    while IFS=';' read -ra parts; do
        for part in "${parts[@]}"; do
            local k="${part%%=*}" v="${part#*=}"
            k="$(printf '%s' "$k" | tr '[:upper:]' '[:lower:]' | tr -d '[:space:]')"
            if [[ "$k" == "$name" ]]; then printf '%s' "$v"; return 0; fi
        done
    done <<<"$CONN"
    return 1
}
DB_HOST="$(conn_part host || printf 'localhost')"
DB_PORT="$(conn_part port || printf '5432')"
DB_NAME="$(conn_part database)" || { note "Database ontbreekt in de connectionstring"; exit 1; }
DB_USER="$(conn_part username || conn_part 'userid')" || { note "Username ontbreekt in de connectionstring"; exit 1; }
DB_PASS="$(conn_part password || true)"
export PGPASSWORD="$DB_PASS"   # only ever passed via environment, never printed
note "Database: $DB_NAME op $DB_HOST:$DB_PORT als gebruiker $DB_USER (wachtwoord niet getoond)."

psql_run() { psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -X -q -A -t -v ON_ERROR_STOP=1 "$@"; }

# --- Step 1: clean working tree ---------------------------------------------
step "Stap 1/18: Git-werkboom controleren (moet schoon zijn)"
if [[ -n "$(git status --porcelain)" ]]; then
    note "De werkboom in $REPO_DIR is niet schoon. Stash of reset eerst bewust; deploy afgebroken."
    exit 1
fi
note "Werkboom is schoon."

# --- Step 2: database backup -------------------------------------------------
step "Stap 2/18: Databaseback-up maken"
BACKUP_PATH="$BACKUP_DIR/${DB_NAME}-$(date '+%Y%m%d-%H%M%S').dump"
pg_dump -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -Fc -f "$BACKUP_PATH"
chmod 600 "$BACKUP_PATH"
note "Back-up geschreven: $BACKUP_PATH ($(du -h "$BACKUP_PATH" | cut -f1))"

# --- Step 3: fetch requested ref ---------------------------------------------
step "Stap 3/18: Gevraagde ref ophalen ($DEPLOY_BRANCH)"
git fetch --prune origin
if git show-ref --verify --quiet "refs/remotes/origin/$DEPLOY_BRANCH"; then
    git checkout -q "$DEPLOY_BRANCH"
    git merge --ff-only "origin/$DEPLOY_BRANCH"
else
    git checkout -q "$DEPLOY_BRANCH"   # commit sha of lokale branch
fi
GIT_COMMIT="$(git rev-parse HEAD)"
note "Op commit: $GIT_COMMIT ($(git log -1 --format='%s' | head -c 80))"

# --- Steps 4-5: backend restore + Release build -------------------------------
step "Stap 4/18: Backend-dependencies herstellen"
dotnet restore "$API_PROJECT/$API_PROJECT.csproj"

step "Stap 5/18: Backend bouwen (Release)"
# Eén Release-build; 'dotnet ef ... --no-build --configuration Release' hergebruikt
# deze output, dus er volgt géén tweede (Debug-)compilatie voor de migraties.
# SourceRevisionId stempelt de commit in de InformationalVersion ("0.2.0+<sha>") —
# de Systeeminformatie-pagina toont zo altijd de ECHT draaiende build.
SHORT_SHA="${GIT_COMMIT:0:8}"
dotnet build "$API_PROJECT/$API_PROJECT.csproj" -c Release --no-restore \
    -p:SourceRevisionId="$SHORT_SHA"

# --- Steps 6-7: frontend -----------------------------------------------------
step "Stap 6/18: Frontend-dependencies installeren"
pushd "$WEB_PROJECT" >/dev/null
npm ci --no-audit --no-fund

step "Stap 7/18: Frontend-productiebuild"
[[ -n "${VITE_API_BASE_URL:-}" ]] || {
    note "VITE_API_BASE_URL ontbreekt in $ENV_FILE — de bundle zou naar de dev-API wijzen."
    exit 1
}
# Buildinfo voor de Systeeminformatie-pagina (frontendversie/commit).
export VITE_BUILD_COMMIT="${GIT_COMMIT:0:8}"
npm run build
popd >/dev/null

# --- Step 8: nginx -----------------------------------------------------------
step "Stap 8/18: nginx-configuratie valideren"
nginx -t

# --- Step 9 is structureel: dezelfde env die systemd gebruikt is hierboven al
# --- geladen en geldt voor ALLE database- en EF-commando's hieronder. --------
step "Stap 9/18: Bevestiging productieomgeving"
note "EF en psql draaien met de omgeving uit $ENV_FILE (identiek aan systemd)."

# --- Step 10: connectivity ---------------------------------------------------
step "Stap 10/18: Databaseconnectiviteit controleren"
psql_run -c "SELECT 1;" >/dev/null
note "Verbinding met $DB_NAME OK."

# --- Ownership preflight (incident 2026-08: tabellen owned door postgres,
# --- migraties draaien als $DB_USER → ALTER TABLE faalde). -------------------
step "Stap 11a/18: Eigenaarschap applicatietabellen controleren (preflight)"
FOREIGN_OWNED="$(psql_run -c "SELECT tablename || ' (eigenaar: ' || tableowner || ')'
    FROM pg_tables
    WHERE schemaname = 'public' AND tableowner <> '$DB_USER'
    ORDER BY tablename;")"
if [[ -n "$FOREIGN_OWNED" ]]; then
    note "FOUT: de volgende public-tabellen zijn NIET van migratiegebruiker '$DB_USER':"
    printf '%s\n' "$FOREIGN_OWNED"
    note "Migraties zouden falen of half toegepast raken. Dit script wijzigt eigenaarschap"
    note "bewust NIET. Laat een beheerder dit eenmalig en gecontroleerd herstellen, bv.:"
    note "  ALTER TABLE public.\"<tabel>\" OWNER TO $DB_USER;   -- per tabel, na review"
    note "en zorg dat provisioning/restores objecten meteen als '$DB_USER' aanmaken"
    note "(pg_restore --no-owner --role=$DB_USER). Zie docs/delivery/operations.md §3.5."
    exit 1
fi
note "Alle public-tabellen zijn van '$DB_USER'."

# --- Step 11b: pending migrations (informative, zonder exitcode-maskering) ---
step "Stap 11b/18: EF-migratiestatus bepalen"
MIGRATIONS_LIST="$(dotnet ef migrations list \
    --project "$API_PROJECT" --startup-project "$API_PROJECT" \
    --no-build --configuration Release)"
PENDING="$(grep '(Pending)' <<<"$MIGRATIONS_LIST" || true)"
if [[ -n "$PENDING" ]]; then
    note "Openstaande migraties (worden zo toegepast, in EF-volgorde):"
    printf '%s\n' "$PENDING"
else
    note "Geen openstaande migraties."
fi

# --- Step 12: apply migrations BEFORE any restart ----------------------------
step "Stap 12/18: EF-migraties toepassen (vóór elke herstart)"
if [[ -n "$PENDING" ]]; then
    MIGRATION_RESULT="MISLUKT"
    dotnet ef database update \
        --project "$API_PROJECT" --startup-project "$API_PROJECT" \
        --no-build --configuration Release
    MIGRATION_RESULT="toegepast: $(grep -c '(Pending)' <<<"$PENDING") migratie(s)"
else
    MIGRATION_RESULT="geen openstaande migraties"
fi
note "Migratieresultaat: $MIGRATION_RESULT"
# Stap 13 (falen = stoppen) is afgedekt door set -e + de ERR-trap: bij een
# migratiefout stopt het script hier, de oude release blijft draaien en de
# back-uplocatie staat in de foutmelding.

# --- Publish new release + switch (previous release preserved) ----------------
step "Stap 14a/18: Backend publiceren naar nieuwe release-map"
RELEASE_DIR="$RELEASES_DIR/$(date '+%Y%m%d-%H%M%S')-${GIT_COMMIT:0:8}"
dotnet publish "$API_PROJECT/$API_PROJECT.csproj" -c Release --no-build -o "$RELEASE_DIR"

# Deployment-metadata voor de Systeeminformatie-pagina: het bestand in de LIVE
# release-map is de waarheid over wat draait — nooit git HEAD (dat is hooguit kandidaat).
APP_VERSION="$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props | head -1 || printf '0.0.0')"
cat > "$RELEASE_DIR/deployment.json" <<METADATA
{
  "version": "$APP_VERSION",
  "commit": "$SHORT_SHA",
  "ref": "$DEPLOY_BRANCH",
  "deployedAtUtc": "$(date -u '+%Y-%m-%dT%H:%M:%SZ')",
  "environment": "${ASPNETCORE_ENVIRONMENT}"
}
METADATA
PREVIOUS_RELEASE="$(readlink -f "$CURRENT_LINK" 2>/dev/null || true)"
ln -sfn "$RELEASE_DIR" "$CURRENT_LINK"
note "Nieuwe release: $RELEASE_DIR"
[[ -n "$PREVIOUS_RELEASE" ]] && note "Vorige release blijft staan: $PREVIOUS_RELEASE"

step "Stap 14b/18: Frontend-assets publiceren"
mkdir -p "$WEB_ROOT"
rsync -a --delete "$WEB_PROJECT/dist/" "$WEB_ROOT/"

step "Stap 14c/18: Service herstarten ($SERVICE_NAME)"
systemctl restart "$SERVICE_NAME"

# --- Steps 15-16: wait + verify ----------------------------------------------
step "Stap 15/18: Wachten op opstart (${STARTUP_WAIT_SECONDS}s max)"
for ((i = 0; i < STARTUP_WAIT_SECONDS; i++)); do
    if systemctl is-active --quiet "$SERVICE_NAME"; then break; fi
    sleep 1
done

step "Stap 16/18: systemd-status controleren"
systemctl is-active --quiet "$SERVICE_NAME" || {
    note "Service is niet actief na herstart. Logs: journalctl -u $SERVICE_NAME -n 100"
    note "Terugrollen kan door de symlink terug te zetten en te herstarten:"
    [[ -n "$PREVIOUS_RELEASE" ]] && note "  ln -sfn $PREVIOUS_RELEASE $CURRENT_LINK && systemctl restart $SERVICE_NAME"
    exit 1
}
SERVICE_STATUS="actief"
note "Service is actief."

# --- Step 17: local health/smoke check ---------------------------------------
step "Stap 17/18: Lokale API-healthcheck"
# De API heeft (bewust) geen /health-endpoint; liveness = de kestrel-poort
# antwoordt met een HTTP-status < 500 op het API-adres.
HEALTH_URL="${DEPLOY_HEALTH_URL:-}"
if [[ -z "$HEALTH_URL" && -n "${ASPNETCORE_URLS:-}" ]]; then
    first_url="${ASPNETCORE_URLS%%;*}"
    HEALTH_URL="$(printf '%s' "$first_url" | sed -E 's#//(0\.0\.0\.0|\*|\+)#//127.0.0.1#')"
fi
HEALTH_URL="${HEALTH_URL:-http://127.0.0.1:5000}"
HEALTH_RESULT="MISLUKT"
HTTP_CODE="000"
for ((i = 0; i < 10; i++)); do
    HTTP_CODE="$(curl -s -o /dev/null -m 5 -w '%{http_code}' "$HEALTH_URL/api/auth/me" || true)"
    if [[ "$HTTP_CODE" =~ ^[1-4][0-9][0-9]$ ]]; then break; fi
    sleep 2
done
if [[ "$HTTP_CODE" =~ ^[1-4][0-9][0-9]$ ]]; then
    HEALTH_RESULT="OK (HTTP $HTTP_CODE op $HEALTH_URL — proces antwoordt; 401 is verwacht zonder token)"
    note "$HEALTH_RESULT"
else
    note "API antwoordt niet gezond (laatste status: $HTTP_CODE op $HEALTH_URL)."
    note "Logs: journalctl -u $SERVICE_NAME -n 100"
    exit 1
fi

# --- Step 18: summary ---------------------------------------------------------
step "Stap 18/18: Samenvatting"
cat <<SUMMARY

================= DEPLOYMENT GESLAAGD =================
Tijdstip          : $(ts)
Git-commit        : $GIT_COMMIT
Release-map       : $RELEASE_DIR
Databaseback-up   : $BACKUP_PATH
Migraties         : $MIGRATION_RESULT
Service           : $SERVICE_NAME — $SERVICE_STATUS
Healthcheck       : $HEALTH_RESULT
=======================================================
SUMMARY
