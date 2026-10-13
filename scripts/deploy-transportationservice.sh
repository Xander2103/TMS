#!/usr/bin/env bash
#
# deploy-transportationservice.sh — production deployment for TransportationService.
#
# Source of truth lives in the repo (scripts/deploy-transportationservice.sh);
# install/update on the server with:
#   sudo install -m 0750 -o root -g root \
#       /var/www/transportationservice/TMS/scripts/deploy-transportationservice.sh \
#       /usr/local/bin/deploy-transportationservice.sh
#
# Run as root:  sudo /usr/local/bin/deploy-transportationservice.sh [git-ref]
#
# Production layout this script is written against (verified 2026-08-28):
#   * repo            /var/www/transportationservice/TMS   (owned by 'deploy')
#   * systemd unit    transportationservice-api, User=deploy, ExecStart =
#                     dotnet .../TransportationService.Api/bin/Release/net10.0/TransportationService.Api.dll
#   * nginx root      .../TransportationService.Web/dist  (served directly)
#   * API             http://127.0.0.1:5019 (proxied under /api/)
#   * env file        /etc/transportationservice/tms.env (root:deploy 0640)
#   * dotnet-ef       /home/deploy/.dotnet/tools/dotnet-ef (global tool of 'deploy')
#
# Hard guarantees (incident 2026-08-28: new code started against an unmigrated
# schema and crashed in AddressMasterBackfillSeeder on locations.AddressExactKey):
#   1. New application binaries NEVER land in the directory systemd starts from
#      while the service can (re)start. The Release build goes to a staging
#      directory; only after `systemctl stop` are the binaries swapped in.
#   2. EF Core migrations are applied AFTER the swap and BEFORE `systemctl start`,
#      with --configuration Release --no-build, i.e. against the exact binaries
#      that will run. A migration failure ends the deployment before any start.
#   3. On failure after the stop, the PREVIOUS binaries are put back and the
#      previous release is started again (code restore only — migrations are
#      never rolled back). The DB backup path is always printed.
#   4. Frontend is built into staging as well; dist/ is replaced only after the
#      API is verified healthy, so a failed build never blanks the live site.
#   5. No secret is ever printed. The env file is loaded, never echoed; psql/pg_dump
#      get the password through a 0600 PGPASSFILE, never on a command line.
#   6. Ownership/privileges are only DETECTED, never changed.
#   7. No git reset/force/push, no destructive DB actions, no tool installs.

set -Eeuo pipefail
IFS=$'\n\t'
umask 027

# --- Configuration (paths only — never credentials) --------------------------
REPO_DIR="${REPO_DIR:-/var/www/transportationservice/TMS}"
DEPLOY_USER="${DEPLOY_USER:-deploy}"
DEPLOY_HOME="${DEPLOY_HOME:-/home/$DEPLOY_USER}"
DEPLOY_BRANCH="${1:-${DEPLOY_BRANCH:-nav-redesign}}"
SERVICE_NAME="${SERVICE_NAME:-transportationservice-api}"
ENV_FILE="${ENV_FILE:-/etc/transportationservice/tms.env}"
BACKUP_DIR="${BACKUP_DIR:-/var/www/transportationservice/backups}"
KEEP_BACKUPS="${KEEP_BACKUPS:-10}"
STAGE_ROOT="${STAGE_ROOT:-/var/www/transportationservice/releases}"
API_PROJECT="TransportationService.Api"
WEB_PROJECT="TransportationService.Web"
TARGET_FRAMEWORK="net10.0"
API_BIN_DIR="$REPO_DIR/$API_PROJECT/bin/Release/$TARGET_FRAMEWORK"
WEB_DIST_DIR="$REPO_DIR/$WEB_PROJECT/dist"
DOTNET_EF="${DOTNET_EF:-$DEPLOY_HOME/.dotnet/tools/dotnet-ef}"
LOCAL_API_URL="${LOCAL_API_URL:-http://127.0.0.1:5019}"
LOCAL_API_PATH="${LOCAL_API_PATH:-/api/auth/me}"
PUBLIC_URL="${PUBLIC_URL:-https://tms-demo.vanmalderstudio.be}"
STARTUP_WAIT_SECONDS="${STARTUP_WAIT_SECONDS:-30}"
LOCK_FILE="/run/lock/deploy-transportationservice.lock"
# Set to 0 to leave the service stopped (with new binaries in place) when a step
# after the stop fails, instead of putting the previous binaries back.
RESTORE_PREVIOUS_ON_FAILURE="${RESTORE_PREVIOUS_ON_FAILURE:-1}"

# --- State for the error handler --------------------------------------------
CURRENT_STEP="init"
BACKUP_PATH="(geen back-up gemaakt)"
MIGRATION_RESULT="niet uitgevoerd"
SERVICE_STOPPED=0
BINARIES_SWAPPED=0
PREV_BIN_DIR=""
STAGE_DIR=""
PGPASSFILE_TMP=""
GIT_COMMIT="(onbekend)"

ts() { date '+%Y-%m-%d %H:%M:%S'; }
step() { CURRENT_STEP="$1"; printf '\n[%s] ==> %s\n' "$(ts)" "$1"; }
note() { printf '[%s]     %s\n' "$(ts)" "$1"; }
fail() { note "FOUT: $1"; exit 1; }

show_service_logs() {
    printf '\n[%s] --- Laatste logregels van %s ---\n' "$(ts)" "$SERVICE_NAME" >&2
    journalctl -u "$SERVICE_NAME" -n 60 --no-pager >&2 || true
    systemctl status "$SERVICE_NAME" --no-pager -l >&2 || true
}

cleanup() {
    [[ -n "$PGPASSFILE_TMP" && -f "$PGPASSFILE_TMP" ]] && rm -f "$PGPASSFILE_TMP"
    return 0
}

on_error() {
    local exit_code=$?
    trap - ERR
    printf '\n[%s] !! DEPLOYMENT MISLUKT in stap: %s (exitcode %d)\n' "$(ts)" "$CURRENT_STEP" "$exit_code" >&2
    printf '[%s] !! Git-commit (kandidaat): %s\n' "$(ts)" "$GIT_COMMIT" >&2
    printf '[%s] !! Database-back-up: %s\n' "$(ts)" "$BACKUP_PATH" >&2
    printf '[%s] !! Migratieresultaat: %s\n' "$(ts)" "$MIGRATION_RESULT" >&2
    printf '[%s] !! Migraties worden NOOIT automatisch teruggedraaid.\n' "$(ts)" >&2

    if (( SERVICE_STOPPED == 1 )); then
        if (( BINARIES_SWAPPED == 1 )) && [[ "$RESTORE_PREVIOUS_ON_FAILURE" == "1" && -n "$PREV_BIN_DIR" && -d "$PREV_BIN_DIR" ]]; then
            printf '[%s] !! Nieuwe binaries worden NIET gestart. Vorige binaries worden teruggezet uit %s\n' "$(ts)" "$PREV_BIN_DIR" >&2
            if rsync -a --delete "$PREV_BIN_DIR/" "$API_BIN_DIR/"; then
                printf '[%s] !! Vorige release wordt opnieuw gestart (%s)\n' "$(ts)" "$SERVICE_NAME" >&2
                systemctl start "$SERVICE_NAME" || true
                sleep 5
                if systemctl is-active --quiet "$SERVICE_NAME"; then
                    printf '[%s] !! Vorige release draait opnieuw. Controleer de site en de logs.\n' "$(ts)" >&2
                else
                    printf '[%s] !! Vorige release start NIET (mogelijk schema deels gemigreerd). Handmatig ingrijpen vereist.\n' "$(ts)" >&2
                    show_service_logs
                fi
            else
                printf '[%s] !! Terugzetten van vorige binaries mislukt. Service blijft GESTOPT. Handmatig herstellen uit: %s\n' "$(ts)" "$PREV_BIN_DIR" >&2
            fi
        elif (( BINARIES_SWAPPED == 1 )); then
            printf '[%s] !! Service blijft GESTOPT met nieuwe binaries in %s. NIET starten vóór de oorzaak vaststaat.\n' "$(ts)" "$API_BIN_DIR" >&2
            printf '[%s] !! Vorige binaries: %s\n' "$(ts)" "${PREV_BIN_DIR:-(geen kopie)}" >&2
        else
            printf '[%s] !! Service was gestopt maar binaries nog niet gewisseld; vorige code wordt opnieuw gestart.\n' "$(ts)" >&2
            systemctl start "$SERVICE_NAME" || true
        fi
    else
        printf '[%s] !! De draaiende service is niet aangeraakt; de vorige release blijft actief.\n' "$(ts)" >&2
    fi
    [[ -n "$STAGE_DIR" ]] && printf '[%s] !! Staging-map blijft staan voor diagnose: %s\n' "$(ts)" "$STAGE_DIR" >&2
    cleanup
    exit "$exit_code"
}
trap on_error ERR
trap cleanup EXIT

require_cmd() { command -v "$1" >/dev/null 2>&1 || fail "vereist commando ontbreekt: $1"; }

# Run a command as the deploy user with the current (already loaded) environment.
# -m keeps the exported env (connection string etc.) without putting it on a command line.
as_deploy() { runuser -u "$DEPLOY_USER" -m -- "$@"; }

# Migration ids present in the source tree (without .cs / .Designer.cs), sorted ascending.
source_migrations() {
    find "$API_PROJECT/Migrations" -maxdepth 1 -type f -name '[0-9]*_*.cs' ! -name '*.Designer.cs' -printf '%f\n' \
        | sed 's/\.cs$//' | grep -E '^[0-9]{14}_' | sort
}

# =============================================================================
step "Stap 1/17: Preflight — vereisten en paden"
[[ "$(id -u)" -eq 0 ]] || fail "dit script moet als root draaien (sudo): systemctl/nginx/back-ups vereisen root."
require_cmd git; require_cmd dotnet; require_cmd npm; require_cmd psql; require_cmd pg_dump
require_cmd curl; require_cmd nginx; require_cmd rsync; require_cmd systemctl; require_cmd runuser
require_cmd journalctl; require_cmd flock
id "$DEPLOY_USER" >/dev/null 2>&1 || fail "deploy-gebruiker '$DEPLOY_USER' bestaat niet."
[[ -d "$REPO_DIR/.git" ]] || fail "repo ontbreekt: $REPO_DIR"
[[ -r "$ENV_FILE" ]] || fail "omgevingsbestand niet leesbaar: $ENV_FILE"
[[ -x "$DOTNET_EF" ]] || fail "dotnet-ef ontbreekt op $DOTNET_EF. Installeer BUITEN een deployment als '$DEPLOY_USER': dotnet tool install --global dotnet-ef"
systemctl cat "$SERVICE_NAME" >/dev/null 2>&1 || fail "systemd-unit '$SERVICE_NAME' bestaat niet."
EXEC_START="$(systemctl show -p ExecStart --value "$SERVICE_NAME")"
[[ "$EXEC_START" == *"$API_BIN_DIR/$API_PROJECT.dll"* ]] \
    || fail "systemd start niet uit $API_BIN_DIR — pas API_BIN_DIR/TARGET_FRAMEWORK aan. ExecStart: $EXEC_START"
mkdir -p "$BACKUP_DIR" "$STAGE_ROOT"
exec 9>"$LOCK_FILE"
flock -n 9 || fail "er loopt al een deployment (lock: $LOCK_FILE)."
EF_VERSION="$(as_deploy "$DOTNET_EF" --version 2>/dev/null | tail -n 1 || true)"
note "dotnet $(dotnet --version), dotnet-ef ${EF_VERSION:-?}, node $(node --version 2>/dev/null || echo '?'), npm $(npm --version)"
cd "$REPO_DIR"

# --- Environment: load the EXACT file systemd uses; values are exported for
# --- children (pg_dump, dotnet-ef, builds) and never printed. ----------------
step "Omgeving laden ($ENV_FILE) — waarden worden nooit getoond"
while IFS= read -r line || [[ -n "$line" ]]; do
    [[ -z "$line" || "$line" =~ ^[[:space:]]*# ]] && continue
    [[ "$line" == *=* ]] || continue
    key="${line%%=*}"; value="${line#*=}"
    key="${key#"${key%%[![:space:]]*}"}"; key="${key%"${key##*[![:space:]]}"}"
    [[ "$key" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] || continue
    if [[ "$value" == \"*\" && "$value" == *\" ]]; then value="${value#\"}"; value="${value%\"}"; fi
    if [[ "$value" == \'*\' && "$value" == *\' ]]; then value="${value#\'}"; value="${value%\'}"; fi
    export "$key=$value"
done < "$ENV_FILE"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_CLI_HOME="$DEPLOY_HOME" HOME="$DEPLOY_HOME"
export PATH="$DEPLOY_HOME/.dotnet/tools:$PATH"
note "Omgeving geladen (ASPNETCORE_ENVIRONMENT=$ASPNETCORE_ENVIRONMENT)."

# --- Parse the Npgsql connection string in memory (never printed). -----------
# psql must NOT be pointed at this string: it is key=value;… (Npgsql), not a libpq URI.
CONN="${ConnectionStrings__DefaultConnection:-}"
[[ -n "$CONN" ]] || fail "ConnectionStrings__DefaultConnection ontbreekt in $ENV_FILE"
conn_part() {
    # conn_part <key...> — first matching key (case/space-insensitive) wins.
    local wanted part k v
    for wanted in "$@"; do
        while IFS= read -r -d ';' part || [[ -n "$part" ]]; do
            k="${part%%=*}"; v="${part#*=}"
            k="$(printf '%s' "$k" | tr '[:upper:]' '[:lower:]' | tr -d '[:space:]')"
            [[ "$k" == "$wanted" ]] && { printf '%s' "$v"; return 0; }
        done <<<"$CONN;"
    done
    return 1
}
DB_HOST="$(conn_part host server || printf 'localhost')"
DB_PORT="$(conn_part port || printf '5432')"
DB_NAME="$(conn_part database || true)"; [[ -n "$DB_NAME" ]] || fail "Database ontbreekt in de connectionstring."
DB_USER="$(conn_part username userid user uid || true)"; [[ -n "$DB_USER" ]] || fail "Username ontbreekt in de connectionstring."
DB_PASS="$(conn_part password pwd || true)"
# Password only ever travels through a 0600 pgpass file (never argv, never stdout).
PGPASSFILE_TMP="$(mktemp /root/.pgpass-deploy.XXXXXX)"
chmod 600 "$PGPASSFILE_TMP"
pgpass_escape() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/:/\\:/g'; }
printf '%s:%s:%s:%s:%s\n' "$(pgpass_escape "$DB_HOST")" "$DB_PORT" "$(pgpass_escape "$DB_NAME")" \
    "$(pgpass_escape "$DB_USER")" "$(pgpass_escape "$DB_PASS")" > "$PGPASSFILE_TMP"
unset DB_PASS
export PGPASSFILE="$PGPASSFILE_TMP"
psql_q() { psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -X -q -A -t -w -v ON_ERROR_STOP=1 "$@"; }
note "Database: $DB_NAME op $DB_HOST:$DB_PORT als '$DB_USER' (wachtwoord niet getoond)."

# =============================================================================
step "Stap 2/17: Git-werkboom moet schoon zijn"
GIT_DIRTY="$(as_deploy git -C "$REPO_DIR" status --porcelain --untracked-files=normal)"
if [[ -n "$GIT_DIRTY" ]]; then
    printf '%s\n' "$GIT_DIRTY" | head -n 20
    fail "werkboom in $REPO_DIR is niet schoon. Ruim bewust op (geen reset door dit script)."
fi
note "Werkboom is schoon (HEAD $(git rev-parse --short HEAD))."

# =============================================================================
step "Stap 3/17: Databaseback-up"
psql_q -c "SELECT 1;" >/dev/null || fail "database niet bereikbaar voor back-up."
BACKUP_PATH="$BACKUP_DIR/${DB_NAME}-$(date '+%Y%m%d-%H%M%S').dump"
pg_dump -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -w -d "$DB_NAME" -Fc -f "$BACKUP_PATH"
chmod 600 "$BACKUP_PATH"
[[ -s "$BACKUP_PATH" ]] || fail "back-up is leeg: $BACKUP_PATH"
note "Back-up: $BACKUP_PATH ($(du -h "$BACKUP_PATH" | cut -f1))"

# =============================================================================
step "Stap 4/17: Git fetch + fast-forward naar $DEPLOY_BRANCH"
as_deploy git -C "$REPO_DIR" fetch --prune origin
if as_deploy git -C "$REPO_DIR" show-ref --verify --quiet "refs/remotes/origin/$DEPLOY_BRANCH"; then
    as_deploy git -C "$REPO_DIR" checkout -q "$DEPLOY_BRANCH"
    as_deploy git -C "$REPO_DIR" merge --ff-only "origin/$DEPLOY_BRANCH"
else
    as_deploy git -C "$REPO_DIR" checkout -q "$DEPLOY_BRANCH"   # commit sha of lokale branch
fi
GIT_COMMIT="$(git rev-parse HEAD)"
SHORT_SHA="${GIT_COMMIT:0:8}"
note "Op commit $GIT_COMMIT — $(git log -1 --format='%s' | head -c 80)"
NEWEST_SOURCE_MIGRATION="$(source_migrations | tail -n 1)"
[[ -n "$NEWEST_SOURCE_MIGRATION" ]] || fail "geen migraties gevonden in $API_PROJECT/Migrations."
note "Nieuwste migratie in de broncode: $NEWEST_SOURCE_MIGRATION"

# =============================================================================
step "Stap 5/17: Backend restore + Release-build naar staging (live map blijft onaangeroerd)"
STAGE_DIR="$STAGE_ROOT/stage-$(date '+%Y%m%d-%H%M%S')-$SHORT_SHA"
STAGE_API="$STAGE_DIR/api"
STAGE_WEB="$STAGE_DIR/web"
install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 0750 "$STAGE_DIR" "$STAGE_API" "$STAGE_WEB"
as_deploy dotnet restore "$API_PROJECT/$API_PROJECT.csproj"
# SourceRevisionId stempelt de commit in de InformationalVersion ("0.2.0+<sha>").
as_deploy dotnet build "$API_PROJECT/$API_PROJECT.csproj" -c Release --no-restore \
    -p:SourceRevisionId="$SHORT_SHA" -o "$STAGE_API"
[[ -f "$STAGE_API/$API_PROJECT.dll" ]] || fail "build leverde geen $API_PROJECT.dll op in $STAGE_API"
APP_VERSION="$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props | head -n 1 || printf '0.0.0')"
cat > "$STAGE_API/deployment.json" <<METADATA
{
  "version": "$APP_VERSION",
  "commit": "$SHORT_SHA",
  "ref": "$DEPLOY_BRANCH",
  "deployedAtUtc": "$(date -u '+%Y-%m-%dT%H:%M:%SZ')",
  "environment": "$ASPNETCORE_ENVIRONMENT"
}
METADATA
chown "$DEPLOY_USER:$DEPLOY_USER" "$STAGE_API/deployment.json"
note "Backend gebouwd in $STAGE_API"

# =============================================================================
step "Stap 6/17: Frontend install + productiebuild naar staging"
if [[ -z "${VITE_API_BASE_URL:-}" ]] && ! grep -qs '^VITE_API_BASE_URL=' "$WEB_PROJECT/.env.production"; then
    fail "VITE_API_BASE_URL is niet gezet (env of $WEB_PROJECT/.env.production) — de bundle zou naar de dev-API wijzen."
fi
export VITE_BUILD_COMMIT="$SHORT_SHA"
( cd "$WEB_PROJECT" && as_deploy npm ci --no-audit --no-fund )
( cd "$WEB_PROJECT" && as_deploy npm run build -- --outDir "$STAGE_WEB" --emptyOutDir )
[[ -f "$STAGE_WEB/index.html" ]] || fail "frontend-build leverde geen index.html op in $STAGE_WEB"
note "Frontend gebouwd in $STAGE_WEB"

# =============================================================================
step "Stap 7/17: nginx-configuratie testen"
nginx -t

# =============================================================================
step "Stap 8/17: Database-preflight (bereikbaarheid, eigenaarschap, rechten, openstaande migraties)"
psql_q -c "SELECT 1;" >/dev/null || fail "database niet bereikbaar."
note "Verbinding OK (server $(psql_q -c 'SHOW server_version;'))."

HISTORY_EXISTS="$(psql_q -c "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL;")"
[[ "$HISTORY_EXISTS" == "t" ]] || fail "tabel __EFMigrationsHistory ontbreekt — dit is geen door EF beheerde productiedatabase?"

# Eigenaarschap: elke public-tabel/-sequence moet van de migratiegebruiker zijn (incident: owner 'postgres').
FOREIGN_OWNED="$(psql_q -c "
    SELECT c.relkind || ' ' || c.relname || ' (eigenaar: ' || pg_get_userbyid(c.relowner) || ')'
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = 'public' AND c.relkind IN ('r','S','v','m','p')
      AND pg_get_userbyid(c.relowner) <> current_user
    ORDER BY c.relname;")"
if [[ -n "$FOREIGN_OWNED" ]]; then
    note "De volgende public-objecten zijn NIET van migratiegebruiker '$DB_USER':"
    printf '%s\n' "$FOREIGN_OWNED"
    note "Migraties zouden falen of half toegepast raken. Dit script wijzigt eigenaarschap bewust NIET."
    note "Laat een beheerder dit gecontroleerd herstellen, bv. per object na review:"
    note "  ALTER TABLE public.\"<tabel>\" OWNER TO $DB_USER;"
    fail "eigenaarschap-preflight mislukt."
fi
SCHEMA_CREATE="$(psql_q -c "SELECT has_schema_privilege(current_user, 'public', 'CREATE');")"
[[ "$SCHEMA_CREATE" == "t" ]] || fail "'$DB_USER' heeft geen CREATE-recht op schema public (nodig voor nieuwe tabellen)."
note "Eigenaarschap en rechten OK voor '$DB_USER'."

APPLIED_COUNT="$(psql_q -c "SELECT count(*) FROM \"__EFMigrationsHistory\";")"
NEWEST_APPLIED="$(psql_q -c "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1;")"
note "Toegepast in database: $APPLIED_COUNT migratie(s), nieuwste: ${NEWEST_APPLIED:-(geen)}"
PENDING_SOURCE="$(source_migrations | awk -v newest="$NEWEST_APPLIED" '$0 > newest' || true)"
PENDING_COUNT=0
if [[ -n "$PENDING_SOURCE" ]]; then
    PENDING_COUNT="$(printf '%s\n' "$PENDING_SOURCE" | grep -c .)"
    note "Openstaande migraties volgens broncode ($PENDING_COUNT) — worden toegepast vóór de herstart:"
    printf '      %s\n' "$PENDING_SOURCE"
else
    note "Geen openstaande migraties volgens broncode."
fi

# =============================================================================
# Vanaf hier ontstaat downtime. Volgorde: stop → binaries wisselen → migreren → start.
# Waarom stoppen vóór het wisselen: systemd heeft Restart=always; zou de oude
# API tussendoor crashen, dan mag hij nooit nieuwe binaries tegen een oud
# schema opstarten. Migreren gebeurt dus terwijl NIETS draait.
# =============================================================================
step "Stap 9/17: Service stoppen ($SERVICE_NAME) — downtime start"
systemctl stop "$SERVICE_NAME"
SERVICE_STOPPED=1
systemctl is-active --quiet "$SERVICE_NAME" && fail "service draait nog na stop."
note "Service gestopt."

step "Stap 10/17: Vorige binaries bewaren en nieuwe binaries plaatsen"
if [[ -d "$API_BIN_DIR" ]]; then
    PREV_BIN_DIR="$STAGE_DIR/previous-api"
    install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 0750 "$PREV_BIN_DIR"
    rsync -a "$API_BIN_DIR/" "$PREV_BIN_DIR/"
    note "Vorige binaries bewaard in $PREV_BIN_DIR"
fi
install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$API_BIN_DIR"
rsync -a --delete --chown="$DEPLOY_USER:$DEPLOY_USER" "$STAGE_API/" "$API_BIN_DIR/"
BINARIES_SWAPPED=1
note "Nieuwe binaries staan in $API_BIN_DIR (service is gestopt)."

# =============================================================================
step "Stap 11/17: EF Core-migraties toepassen (Release, --no-build, vóór elke start)"
# dotnet-ef --no-build gebruikt de assembly in bin/Release/net10.0 — exact wat systemd straks start.
MIGRATIONS_LIST="$(as_deploy "$DOTNET_EF" migrations list \
    --project "$API_PROJECT" --startup-project "$API_PROJECT" \
    --configuration Release --no-build --no-color --prefix-output 2>&1)" \
    || { printf '%s\n' "$MIGRATIONS_LIST" | tail -n 30; fail "dotnet ef migrations list mislukt."; }
grep -q "$NEWEST_SOURCE_MIGRATION" <<<"$MIGRATIONS_LIST" \
    || fail "gebouwde assembly kent migratie $NEWEST_SOURCE_MIGRATION niet — build komt niet overeen met de gepullde code."
EF_PENDING="$(grep -E '\(Pending\)' <<<"$MIGRATIONS_LIST" | sed -E 's/^[^:]*:[[:space:]]*//' || true)"
if [[ -n "$EF_PENDING" ]]; then
    note "EF meldt openstaande migraties:"
    printf '      %s\n' "$EF_PENDING"
    MIGRATION_RESULT="MISLUKT (zie foutmelding hierboven)"
    as_deploy "$DOTNET_EF" database update \
        --project "$API_PROJECT" --startup-project "$API_PROJECT" \
        --configuration Release --no-build --no-color
    MIGRATION_RESULT="toegepast: $(grep -c . <<<"$EF_PENDING") migratie(s)"
else
    MIGRATION_RESULT="geen openstaande migraties"
fi
NEWEST_APPLIED_AFTER="$(psql_q -c "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1;")"
[[ "$NEWEST_APPLIED_AFTER" == "$NEWEST_SOURCE_MIGRATION" ]] \
    || fail "na migreren is de nieuwste toegepaste migratie '$NEWEST_APPLIED_AFTER', verwacht '$NEWEST_SOURCE_MIGRATION'."
note "Migratieresultaat: $MIGRATION_RESULT — schema staat op $NEWEST_APPLIED_AFTER."

# =============================================================================
step "Stap 12/17: Service starten met gemigreerd schema"
systemctl start "$SERVICE_NAME"

step "Stap 13/17: Wachten op opstart (max ${STARTUP_WAIT_SECONDS}s)"
HTTP_CODE="000"
for ((i = 0; i < STARTUP_WAIT_SECONDS; i++)); do
    if ! systemctl is-active --quiet "$SERVICE_NAME"; then
        if systemctl is-failed --quiet "$SERVICE_NAME"; then break; fi
    else
        HTTP_CODE="$(curl -s -o /dev/null -m 3 -w '%{http_code}' "$LOCAL_API_URL$LOCAL_API_PATH" || true)"
        case "$HTTP_CODE" in 200|204|400|401|403|404|405) break ;; esac
    fi
    sleep 1
done
systemctl is-active --quiet "$SERVICE_NAME" || { show_service_logs; fail "service is niet actief na start."; }
note "Service actief ($(systemctl show -p MainPID --value "$SERVICE_NAME" | sed 's/^/PID /'))."

step "Stap 14/17: Lokale API-check ($LOCAL_API_URL$LOCAL_API_PATH)"
case "$HTTP_CODE" in
    200|204|400|401|403|404|405) ;;
    *)
        for ((i = 0; i < 10; i++)); do
            HTTP_CODE="$(curl -s -o /dev/null -m 5 -w '%{http_code}' "$LOCAL_API_URL$LOCAL_API_PATH" || true)"
            case "$HTTP_CODE" in 200|204|400|401|403|404|405) break ;; esac
            sleep 2
        done ;;
esac
case "$HTTP_CODE" in
    200|204|400|401|403|404|405) HEALTH_RESULT="OK (HTTP $HTTP_CODE; 401 is verwacht zonder token)"; note "$HEALTH_RESULT" ;;
    *) show_service_logs; fail "API antwoordt niet gezond (laatste status: $HTTP_CODE)." ;;
esac

# =============================================================================
step "Stap 15/17: Frontend live zetten + nginx herladen"
install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$WEB_DIST_DIR"
rsync -a --delete --chown="$DEPLOY_USER:$DEPLOY_USER" "$STAGE_WEB/" "$WEB_DIST_DIR/"
nginx -t
systemctl reload nginx
note "Frontend staat in $WEB_DIST_DIR; nginx herladen."

step "Stap 16/17: Publieke website-check ($PUBLIC_URL)"
PUBLIC_CODE="000"
for ((i = 0; i < 10; i++)); do
    PUBLIC_CODE="$(curl -s -o /dev/null -m 10 -L -w '%{http_code}' "$PUBLIC_URL/" || true)"
    [[ "$PUBLIC_CODE" == "200" ]] && break
    sleep 2
done
[[ "$PUBLIC_CODE" == "200" ]] || fail "publieke site geeft HTTP $PUBLIC_CODE (verwacht 200)."
PUBLIC_API_CODE="$(curl -s -o /dev/null -m 10 -w '%{http_code}' "$PUBLIC_URL$LOCAL_API_PATH" || true)"
note "Publieke site: HTTP 200; publieke API-proxy: HTTP $PUBLIC_API_CODE."

# =============================================================================
step "Stap 17/17: Opruimen (oude back-ups en staging)"
# Retentie: de nieuwste $KEEP_BACKUPS dumps blijven staan.
mapfile -t OLD_BACKUPS < <(find "$BACKUP_DIR" -maxdepth 1 -type f -name "$DB_NAME-*.dump" -printf '%T@ %p\n' \
    | sort -rn | cut -d' ' -f2- | tail -n +"$((KEEP_BACKUPS + 1))")
for f in "${OLD_BACKUPS[@]:-}"; do
    [[ -n "$f" && -f "$f" ]] && { rm -f -- "$f"; note "Oude back-up verwijderd: $f"; }
done
# Staging: nieuwste 3 blijven staan (bevatten 'previous-api' voor snel herstel).
mapfile -t OLD_STAGES < <(find "$STAGE_ROOT" -maxdepth 1 -type d -name 'stage-*' -printf '%T@ %p\n' \
    | sort -rn | cut -d' ' -f2- | tail -n +4)
for d in "${OLD_STAGES[@]:-}"; do
    [[ -n "$d" && -d "$d" ]] && rm -rf -- "$d"
done
note "Back-ups bewaard: $(find "$BACKUP_DIR" -maxdepth 1 -type f -name "$DB_NAME-*.dump" | wc -l) (max $KEEP_BACKUPS)."

cat <<SUMMARY

================= DEPLOYMENT GESLAAGD =================
Tijdstip          : $(ts)
Domein            : $PUBLIC_URL  (HTTP $PUBLIC_CODE)
Git-commit        : $GIT_COMMIT ($DEPLOY_BRANCH)
Versie            : $APP_VERSION+$SHORT_SHA
Databaseback-up   : $BACKUP_PATH
Migraties         : $MIGRATION_RESULT
Schema            : $NEWEST_APPLIED_AFTER
Service           : $SERVICE_NAME — actief
Lokale API        : $HEALTH_RESULT
Vorige binaries   : ${PREV_BIN_DIR:-(geen)}
=======================================================
SUMMARY
