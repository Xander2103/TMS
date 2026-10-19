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
# Run as root:  sudo /usr/local/bin/deploy-transportationservice.sh <git-ref>
#   <git-ref> is MANDATORY (prefer an exact commit sha). There is no default branch.
#
# Production layout this script is written against (docs/delivery/operations.md §3):
#   * repo            /var/www/transportationservice/TMS   (owned by 'deploy')
#   * systemd unit    transportationservice-api (deploy/systemd/transportationservice-api.service),
#                     User=deploy, WorkingDirectory = API_BIN_DIR, ExecStart =
#                     dotnet <API_BIN_DIR>/TransportationService.Api.dll
#   * API_BIN_DIR     .../TransportationService.Api/bin/Release/net10.0  (release dir)
#   * nginx root      .../TransportationService.Web/dist  (deploy/nginx/transportationservice.conf)
#   * API             http://127.0.0.1:5019, readiness GET /health/ready (200 = ready)
#   * env file        /etc/transportationservice/tms.env (root:deploy 0640)
#   * storage root    Storage__RootPath (absolute, OUTSIDE every release dir), e.g.
#                     /var/lib/transportationservice/storage — never touched by this script
#   * dotnet-ef       /home/deploy/.dotnet/tools/dotnet-ef (global tool of 'deploy')
#
# Order (every step before "downtime" is read-only for the live system):
#    1  preflight: tools, paths, unit, lock, git ref
#    2  storage preflight: Storage__RootPath set, absolute, outside release dirs, exists,
#       writable by the service user; legacy App_Data in the bin dir => stop
#    3  database connection + server version (password only through a 0600 PGPASSFILE)
#    4  git fetch + checkout of the requested ref (clean tree required); schema not newer than
#       the code; exact pending set (RESTORE_PREVIOUS_ON_FAILURE=1 + pending => refused)
#    5  backend build into staging        (clean environment: NO tms.env secrets)
#    6  frontend build into staging       (clean environment: only VITE_API_BASE_URL)
#    7  nginx -t
#    8  DB preflight: ownership, CREATE privilege, master-data/fleet duplicates, pending
#       migrations confirmed by EF against the STAGED build
#   --- downtime ---
#    9  maintenance page on + systemctl stop
#   10  final pg_dump (service stopped => final state) + pg_restore --list verification
#       + storage snapshot (tar, excl. backups/ and message-sink/) + sha256
#   11  dotnet-ef database update (staged build, Command Timeout + lock_timeout, per-migration timing)
#   12  validate: 0 pending, history == source
#   13  install binaries (previous copy kept; rsync --delete NEVER touches App_Data/)
#   14  systemctl start + wait for GET /health/ready == 200
#   15  frontend live (previous dist kept), nginx reload, maintenance page off
#   --- live ---
#   16  public check   17  prune
#
# FORWARD-ONLY (production-readiness B2/MIG-01): once the migration step has started, or the
# new binaries have been started once, the previous binaries are NEVER started again by this
# script. On such a failure the service is left STOPPED with the maintenance page ON and the
# restore-based rollback runbook is printed. Only a failure before any migration ran (and before
# the new binaries ran) restarts the untouched previous release, and only if it was running.
# RESTORE_PREVIOUS_ON_FAILURE=1 (default 0) only applies to a release WITHOUT migrations whose
# binary install failed before the first start; with pending migrations it is refused.
#
# Hard guarantees:
#   * No secret is ever printed. tms.env is never exported into this shell: builds get a clean
#     environment; dotnet-ef gets only ASPNETCORE_ENVIRONMENT + the connection string (env, never
#     argv); psql/pg_dump get the password via a 0600 PGPASSFILE.
#   * Ownership/privileges are only DETECTED, never changed. No git reset/force/push, no
#     destructive DB actions, no tool installs, no file moves in the storage root.
#
# Local simulation (macOS/Linux, no root): DEPLOY_SIMULATE=1 plus explicit sandbox paths and
# command overrides — see scripts/deploy-sim/run.sh. Simulation refuses the production paths.

set -euo pipefail
IFS=$'\n\t'
umask 027

usage() {
    cat <<'USAGE'
Gebruik: sudo deploy-transportationservice.sh <git-ref>

  <git-ref>   verplicht: exacte commit-sha (aanbevolen), tag of branch. Geen default.

Belangrijkste omgevingsvariabelen (paden/gedrag, nooit secrets):
  RESTORE_PREVIOUS_ON_FAILURE=0   forward-only (default). 1 wordt geweigerd bij openstaande migraties.
  STARTUP_WAIT_SECONDS=300        max. wachttijd op GET /health/ready na de start.
  MIGRATION_LOCK_TIMEOUT=60s      lock_timeout voor de migratiestap.
  MIGRATION_COMMAND_TIMEOUT=0     Npgsql Command Timeout voor de migratiestap (0 = geen limiet).
  STORAGE_SNAPSHOT=1              tar-snapshot van Storage__RootPath na de dump (0 = overslaan).
  MAINTENANCE_FLAG=...            vlagbestand dat nginx de onderhoudspagina laat tonen.
Zie docs/delivery/operations.md §3 voor de volledige lijst.
USAGE
}

case "${1:-}" in -h|--help) usage; exit 0 ;; esac

# --- Configuration (paths/behaviour only — never credentials) ---------------
DEPLOY_SIMULATE="${DEPLOY_SIMULATE:-0}"
REPO_DIR="${REPO_DIR:-/var/www/transportationservice/TMS}"
DEPLOY_USER="${DEPLOY_USER:-deploy}"
DEPLOY_HOME="${DEPLOY_HOME:-/home/$DEPLOY_USER}"
DEPLOY_REF="${1:-}"
SERVICE_NAME="${SERVICE_NAME:-transportationservice-api}"
ENV_FILE="${ENV_FILE:-/etc/transportationservice/tms.env}"
BACKUP_DIR="${BACKUP_DIR:-/var/www/transportationservice/backups}"
KEEP_BACKUPS="${KEEP_BACKUPS:-10}"
KEEP_STORAGE_SNAPSHOTS="${KEEP_STORAGE_SNAPSHOTS:-3}"
KEEP_STAGES="${KEEP_STAGES:-3}"
STAGE_ROOT="${STAGE_ROOT:-/var/www/transportationservice/releases}"
API_PROJECT="TransportationService.Api"
WEB_PROJECT="TransportationService.Web"
TARGET_FRAMEWORK="${TARGET_FRAMEWORK:-net10.0}"
API_BIN_DIR="${API_BIN_DIR:-$REPO_DIR/$API_PROJECT/bin/Release/$TARGET_FRAMEWORK}"
WEB_DIST_DIR="${WEB_DIST_DIR:-$REPO_DIR/$WEB_PROJECT/dist}"
MAINTENANCE_MODE="${MAINTENANCE_MODE:-1}"
MAINTENANCE_FLAG="${MAINTENANCE_FLAG:-/var/www/transportationservice/maintenance/maintenance.on}"
LOCAL_API_URL="${LOCAL_API_URL:-http://127.0.0.1:5019}"
READY_PATH="${READY_PATH:-/health/ready}"
PUBLIC_URL="${PUBLIC_URL:-}"
STARTUP_WAIT_SECONDS="${STARTUP_WAIT_SECONDS:-300}"
MAX_START_RESTARTS="${MAX_START_RESTARTS:-3}"
MIGRATION_LOCK_TIMEOUT="${MIGRATION_LOCK_TIMEOUT:-60s}"
MIGRATION_COMMAND_TIMEOUT="${MIGRATION_COMMAND_TIMEOUT:-0}"
MIGRATION_VERBOSE="${MIGRATION_VERBOSE:-0}"
STORAGE_SNAPSHOT="${STORAGE_SNAPSHOT:-1}"
RESTORE_PREVIOUS_ON_FAILURE="${RESTORE_PREVIOUS_ON_FAILURE:-0}"
LOCK_FILE="${LOCK_FILE:-/run/lock/deploy-transportationservice.lock}"
PGPASS_DIR="${PGPASS_DIR:-/root}"
# External commands — overridable for the local simulation harness (scripts/deploy-sim/).
GIT="${GIT:-git}"
DOTNET="${DOTNET:-dotnet}"
DOTNET_EF="${DOTNET_EF:-$DEPLOY_HOME/.dotnet/tools/dotnet-ef}"
NPM="${NPM:-npm}"
PSQL="${PSQL:-psql}"
PG_DUMP="${PG_DUMP:-pg_dump}"
PG_RESTORE="${PG_RESTORE:-pg_restore}"
SYSTEMCTL="${SYSTEMCTL:-systemctl}"
JOURNALCTL="${JOURNALCTL:-journalctl}"
NGINX="${NGINX:-nginx}"
CURL="${CURL:-curl}"
RSYNC="${RSYNC:-rsync}"
TAR="${TAR:-tar}"
RUNUSER="${RUNUSER:-runuser}"
# Build environment: PATH is inherited (not secret); nothing from tms.env.
BUILD_PATH="${BUILD_PATH:-$DEPLOY_HOME/.dotnet/tools:$PATH}"

# --- State for the exit handler ---------------------------------------------
CURRENT_STEP="init"
DEPLOY_DONE=0
RELEASE_LIVE=0
DOWNTIME_STARTED=0
MAINTENANCE_ON=0
SERVICE_WAS_ACTIVE=0
SERVICE_STOPPED=0
MIGRATION_STARTED=0
UNIT_DISABLED=0
BINARIES_INSTALL_STARTED=0
NEW_BINARIES_STARTED=0
BACKUP_PATH="(geen back-up gemaakt)"
BACKUP_TIME=""
STORAGE_SNAPSHOT_PATH="(geen snapshot gemaakt)"
CHECKSUM_PATH=""
MIGRATION_RESULT="niet uitgevoerd"
MIGRATION_LOG=""
PREV_BIN_DIR=""
APPLIED_COUNT=0
PENDING_COUNT=0
PREV_WEB_DIR=""
STAGE_DIR=""
PGPASSFILE_TMP=""
LOCK_DIR_CREATED=""
GIT_COMMIT="(onbekend)"
DB_NAME=""
STORAGE_ROOT=""

ts() { date '+%Y-%m-%d %H:%M:%S'; }
step() { CURRENT_STEP="$1"; printf '\n[%s] ==> %s\n' "$(ts)" "$1"; }
note() { printf '[%s]     %s\n' "$(ts)" "$1"; }
warn() { printf '[%s]     LET OP: %s\n' "$(ts)" "$1"; }
fail() { printf '[%s]     FOUT: %s\n' "$(ts)" "$1" >&2; exit 1; }
refuse() { printf '[%s]     GEWEIGERD: %s\n' "$(ts)" "$1" >&2; exit 2; }
emsg() { printf '[%s] !! %s\n' "$(ts)" "$1" >&2; }
simulating() { [[ "$DEPLOY_SIMULATE" == "1" ]]; }

show_service_logs() {
    printf '\n[%s] --- Laatste logregels van %s ---\n' "$(ts)" "$SERVICE_NAME" >&2
    "$JOURNALCTL" -u "$SERVICE_NAME" -n 60 --no-pager >&2 || true
    "$SYSTEMCTL" status "$SERVICE_NAME" --no-pager -l >&2 || true
}

service_active() { "$SYSTEMCTL" is-active --quiet "$SERVICE_NAME"; }

maintenance_on() {
    [[ "$MAINTENANCE_MODE" == "1" ]] || return 0
    mkdir -p "$(dirname "$MAINTENANCE_FLAG")"
    printf 'deploy %s %s\n' "$GIT_COMMIT" "$(ts)" > "$MAINTENANCE_FLAG"
    chmod 0644 "$MAINTENANCE_FLAG"
    MAINTENANCE_ON=1
}
maintenance_off() {
    [[ "$MAINTENANCE_MODE" == "1" ]] || return 0
    rm -f "$MAINTENANCE_FLAG"
    MAINTENANCE_ON=0
}

# Run as the deploy user, keeping the (already cleaned) environment of the caller.
as_deploy() {
    if simulating; then "$@"; else "$RUNUSER" -u "$DEPLOY_USER" -m -- "$@"; fi
}
as_service_user() {
    if simulating; then "$@"; else "$RUNUSER" -u "$SERVICE_USER" -- "$@"; fi
}
# run_clean KEY=VALUE... -- command args
# Runs a command as the deploy user in a subshell whose EXPORTED environment is reduced to a
# whitelist, plus the given KEY=VALUE pairs. Other variables lose their export attribute
# (export -n) so the child process never receives them while this script's own settings stay
# usable. The pairs are exported by the shell builtin, so values (e.g. the connection string)
# never appear on any process command line.
run_clean() {
    (
        local v
        for v in $(compgen -e); do
            case "$v" in PATH|LANG|LC_ALL|LC_CTYPE|TERM|DOTNET_ROOT|TMPDIR) ;; *) export -n "$v" 2>/dev/null || true ;; esac
        done
        export PATH="$BUILD_PATH" HOME="$DEPLOY_HOME" DOTNET_CLI_HOME="$DEPLOY_HOME" \
               DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
               MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
        while [[ $# -gt 0 && "$1" != "--" ]]; do export "$1"; shift; done
        [[ "${1:-}" == "--" ]] && shift
        as_deploy "$@"
    )
}
install_dir() {
    if simulating; then mkdir -p "$@"; else install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 0750 "$@"; fi
}
rsync_owned() {
    if simulating; then "$RSYNC" "$@"; else "$RSYNC" --chown="$DEPLOY_USER:$DEPLOY_USER" "$@"; fi
}
sha256_files() {
    if command -v sha256sum >/dev/null 2>&1; then sha256sum "$@"; else shasum -a 256 "$@"; fi
}

# Read ONE key from the EnvironmentFile without exporting anything (same parsing rules as
# before: KEY=VALUE, optional surrounding quotes, comments/blank lines ignored; last one wins).
env_file_value() {
    local wanted="$1" line key value found=""
    while IFS= read -r line || [[ -n "$line" ]]; do
        [[ -z "$line" || "$line" =~ ^[[:space:]]*# ]] && continue
        [[ "$line" == *=* ]] || continue
        key="${line%%=*}"; value="${line#*=}"
        key="${key#"${key%%[![:space:]]*}"}"; key="${key%"${key##*[![:space:]]}"}"
        [[ "$key" == "$wanted" ]] || continue
        if [[ "$value" == \"*\" && "$value" == *\" ]]; then value="${value#\"}"; value="${value%\"}"; fi
        if [[ "$value" == \'*\' && "$value" == *\' ]]; then value="${value#\'}"; value="${value%\'}"; fi
        found="$value"
    done < "$ENV_FILE"
    printf '%s' "$found"
}

# Migration ids present in the source tree (without .cs / .Designer.cs), sorted ascending.
source_migrations() {
    find "$API_PROJECT/Migrations" -maxdepth 1 -type f -name '[0-9]*_*.cs' ! -name '*.Designer.cs' \
        | sed -e 's#.*/##' -e 's/\.cs$//' | grep -E '^[0-9]{14}_' | sort
}

# Normalise a path textually (no symlink resolution): squeeze //, drop trailing /.
norm_path() { local p; p="$(printf '%s' "$1" | tr -s '/')"; [[ "$p" == "/" ]] || p="${p%/}"; printf '%s' "$p"; }
# Physical path of an existing directory, else the textual one.
phys_path() { if [[ -d "$1" ]]; then (cd "$1" && pwd -P); else norm_path "$1"; fi; }
# is_within CHILD PARENT — true when CHILD equals PARENT or lies below it (textual + physical).
is_within() {
    local c p
    for c in "$(norm_path "$1")" "$(phys_path "$1")"; do
        for p in "$(norm_path "$2")" "$(phys_path "$2")"; do
            [[ "$p" == "/" || "$c/" == "$p/"* ]] && return 0
        done
    done
    return 1
}

cleanup() {
    [[ -n "$PGPASSFILE_TMP" && -f "$PGPASSFILE_TMP" ]] && rm -f "$PGPASSFILE_TMP"
    [[ -n "$LOCK_DIR_CREATED" && -d "$LOCK_DIR_CREATED" ]] && rmdir "$LOCK_DIR_CREATED" 2>/dev/null
    return 0
}

print_rollback_runbook() {
    emsg "Volgende stap — kies bewust één route (docs/delivery/operations.md §3.7 en §7):"
    emsg "  A) VOORUIT HERSTELLEN (voorkeur bij een data- of configuratieprobleem):"
    emsg "     los de oorzaak op (fout hierboven; journalctl -u $SERVICE_NAME) en draai deze deploy"
    emsg "     opnieuw met DEZELFDE ref: sudo $(basename "$0") $DEPLOY_REF"
    emsg "     (EF hervat bij de eerste niet-toegepaste migratie; de service blijft tot dan gestopt)."
    emsg "  B) TERUGROLLEN VIA RESTORE (nooit code-only):"
    emsg "     1. Database: pg_restore van $BACKUP_PATH naar een NIEUWE database"
    emsg "        (createdb -O <eigenaar> ${DB_NAME:-<db>}_rb_<ts>; pg_restore --no-owner --role=<eigenaar> --exit-on-error -d ...),"
    emsg "        daarna Database= in ConnectionStrings__DefaultConnection ($ENV_FILE) daarop zetten."
    emsg "        Volledige procedure: docs/operations/backup-restore.md. De kapotte database NOOIT droppen."
    if [[ -n "$PREV_BIN_DIR" ]]; then
        emsg "     2. Binaries: $RSYNC -a --delete --exclude=/App_Data/ $PREV_BIN_DIR/ $API_BIN_DIR/"
    else
        emsg "     2. Binaries: ongewijzigd — de vorige release staat nog in $API_BIN_DIR."
    fi
    if [[ -n "$PREV_WEB_DIR" ]]; then
        emsg "     3. Frontend: $RSYNC -a --delete $PREV_WEB_DIR/ $WEB_DIST_DIR/ && nginx -s reload"
    else
        emsg "     3. Frontend: ongewijzigd — de vorige dist/ is nog live."
    fi
    emsg "     4. Bestanden: Storage__RootPath NIET terugzetten (snapshot $STORAGE_SNAPSHOT_PATH is enkel"
    emsg "        voor rampherstel); noteer uploads van na $BACKUP_TIME als wees."
    emsg "     5. systemctl enable $SERVICE_NAME && systemctl start $SERVICE_NAME; wacht op $LOCAL_API_URL$READY_PATH = 200;"
    emsg "        controleer Systeeminformatie; daarna onderhoud uit: rm -f $MAINTENANCE_FLAG"
    emsg "  NOOIT: de vorige binaries starten op dit schema, of dotnet ef database update <oudere migratie>."
    if (( UNIT_DISABLED == 1 )); then
        emsg "  De unit is UITGESCHAKELD (systemctl disable): een reboot start niets. Zet hem pas na het"
        emsg "  herstel weer aan met 'systemctl enable $SERVICE_NAME' (stap 5 hierboven)."
    else
        emsg "  LET OP: 'systemctl disable $SERVICE_NAME' is mislukt — de unit is nog 'enabled' en een"
        emsg "  reboot zou de geïnstalleerde binaries starten. Schakel hem nu manueel uit."
    fi
}

on_exit() {
    local exit_code=$?
    set +e
    trap - EXIT
    if (( DEPLOY_DONE == 1 || exit_code == 0 )); then cleanup; exit "$exit_code"; fi

    printf '\n[%s] !! DEPLOYMENT MISLUKT in stap: %s (exitcode %d)\n' "$(ts)" "$CURRENT_STEP" "$exit_code" >&2
    emsg "Git-commit (kandidaat): $GIT_COMMIT"
    emsg "Databaseback-up: $BACKUP_PATH${BACKUP_TIME:+ (genomen $BACKUP_TIME, service gestopt; geverifieerd met pg_restore --list)}"
    emsg "Opslag-snapshot: $STORAGE_SNAPSHOT_PATH${CHECKSUM_PATH:+ (checksums: $CHECKSUM_PATH)}"
    emsg "Migratieresultaat: $MIGRATION_RESULT${MIGRATION_LOG:+ (log: $MIGRATION_LOG)}"
    emsg "Migraties worden NOOIT automatisch teruggedraaid."

    if (( RELEASE_LIVE == 1 )); then
        emsg "De nieuwe release is LIVE (API gezond, frontend gewisseld). Alleen een nacontrole faalde."
        emsg "Er wordt niets teruggedraaid; onderzoek de fout hierboven."
    elif (( DOWNTIME_STARTED == 0 )); then
        emsg "De draaiende service en de database zijn NIET aangeraakt; de vorige release blijft actief."
    elif (( MIGRATION_STARTED == 0 && NEW_BINARIES_STARTED == 0 && BINARIES_INSTALL_STARTED == 0 )); then
        # Nothing replaced, schema untouched: undo the stop (this is not a rollback).
        emsg "Er is GEEN migratie gestart en de binaries zijn niet vervangen."
        if (( SERVICE_WAS_ACTIVE == 1 )); then
            emsg "De ongewijzigde vorige release wordt opnieuw gestart ($SERVICE_NAME)."
            "$SYSTEMCTL" start "$SERVICE_NAME"
            sleep 3
            if service_active; then
                maintenance_off
                emsg "Vorige release draait opnieuw; onderhoudspagina uit. Controleer de site."
            else
                emsg "Vorige release start NIET. Onderhoudspagina blijft aan. Handmatig ingrijpen vereist."
                show_service_logs
            fi
        else
            emsg "De service draaide vóór de deploy niet; hij blijft gestopt (onderhoudspagina blijft aan)."
        fi
    elif (( MIGRATION_STARTED == 0 && NEW_BINARIES_STARTED == 0 )); then
        # Release without migrations; the binary install failed before the first start.
        "$SYSTEMCTL" stop "$SERVICE_NAME" >/dev/null 2>&1
        if [[ "$RESTORE_PREVIOUS_ON_FAILURE" == "1" && -n "$PREV_BIN_DIR" && -d "$PREV_BIN_DIR" ]]; then
            emsg "Geen migratie toegepast en nieuwe binaries nooit gestart: vorige binaries terugzetten uit $PREV_BIN_DIR."
            if "$RSYNC" -a --delete --exclude=/App_Data/ "$PREV_BIN_DIR/" "$API_BIN_DIR/"; then
                if (( SERVICE_WAS_ACTIVE == 1 )); then
                    "$SYSTEMCTL" start "$SERVICE_NAME"
                    sleep 3
                    if service_active; then maintenance_off; emsg "Vorige release draait opnieuw."
                    else emsg "Vorige release start NIET. Handmatig ingrijpen vereist."; show_service_logs; fi
                fi
            else
                emsg "Terugzetten mislukt. Service blijft GESTOPT; onderhoudspagina blijft aan."
            fi
        else
            emsg "Service blijft GESTOPT (binaries mogelijk half gewisseld in $API_BIN_DIR); onderhoudspagina blijft aan."
            emsg "Geen migratie toegepast: herstel met $RSYNC -a --delete --exclude=/App_Data/ ${PREV_BIN_DIR:-<vorige-binaries>}/ $API_BIN_DIR/"
            emsg "en systemctl start $SERVICE_NAME (of zet RESTORE_PREVIOUS_ON_FAILURE=1 bij een release zonder migraties)."
        fi
    else
        # FORWARD-ONLY: a migration ran (or was attempted) or the new code already ran.
        "$SYSTEMCTL" stop "$SERVICE_NAME" >/dev/null 2>&1
        # A reboot must not start whatever binaries are in place on the (partly) new schema
        # either: disable the unit until the operator has finished the recovery.
        "$SYSTEMCTL" disable "$SERVICE_NAME" >/dev/null 2>&1 \
            && UNIT_DISABLED=1
        emsg "=== FORWARD-ONLY: de vorige binaries worden NIET gestart ==="
        if service_active; then
            emsg "WAARSCHUWING: $SERVICE_NAME is nog actief na stop — controleer onmiddellijk."
        else
            if (( MAINTENANCE_ON == 1 )); then
                emsg "Service $SERVICE_NAME is GESTOPT en blijft gestopt; onderhoudspagina staat AAN ($MAINTENANCE_FLAG)."
            else
                emsg "Service $SERVICE_NAME is GESTOPT en blijft gestopt (MAINTENANCE_MODE=0: nginx geeft 502 op /api)."
            fi
        fi
        if (( MIGRATION_STARTED == 1 )); then
            emsg "Reden: de migratiestap is gestart; EF commit per migratie, dus het schema kan (deels) nieuw zijn."
            local now_count
            PSQL_PGOPTIONS="-c lock_timeout=5s -c statement_timeout=15s"
            now_count="$(psql_q -c "SELECT count(*) FROM \"__EFMigrationsHistory\";" 2>/dev/null)"
            if [[ "$now_count" =~ ^[0-9]+$ ]]; then
                emsg "In deze run toegepast: $((now_count - APPLIED_COUNT)) van $PENDING_COUNT openstaande migratie(s); schema staat op $(psql_q -c "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1 DESC LIMIT 1;" 2>/dev/null)."
            else
                emsg "Migratiehistoriek nu niet leesbaar (lock of DB onbereikbaar); controleer later: SELECT count(*) FROM \"__EFMigrationsHistory\" (vóór de deploy: $APPLIED_COUNT)."
            fi
        else
            emsg "Reden: de nieuwe binaries zijn al gestart (startup-seeders schreven naar de database)."
        fi
        emsg "Oude code op het nieuwe schema breekt aantoonbaar (production-readiness B2/MIG-01)."
        if [[ "$RESTORE_PREVIOUS_ON_FAILURE" == "1" ]]; then
            emsg "GEWEIGERD: RESTORE_PREVIOUS_ON_FAILURE=1 wordt genegeerd — code-only rollback na een migratie"
            emsg "of na de eerste start van de nieuwe code is niet toegestaan. Gebruik route B hieronder."
        fi
        if [[ -n "$PREV_BIN_DIR" ]]; then
            emsg "Nieuwe binaries staan in $API_BIN_DIR; vorige binaries bewaard in $PREV_BIN_DIR."
        fi
        print_rollback_runbook
    fi
    [[ -n "$STAGE_DIR" ]] && emsg "Staging-map blijft staan voor diagnose: $STAGE_DIR"
    cleanup
    exit "$exit_code"
}
trap on_exit EXIT

require_cmd() { command -v "$1" >/dev/null 2>&1 || fail "vereist commando ontbreekt: $1"; }

# =============================================================================
step "Stap 1/17: Preflight — vereisten, paden en git-ref"
if [[ -z "$DEPLOY_REF" ]]; then
    usage >&2
    refuse "geen git-ref opgegeven. Geef expliciet de te deployen commit-sha (er is bewust geen default)."
fi
[[ "$DEPLOY_REF" =~ ^[A-Za-z0-9._/-]+$ && "$DEPLOY_REF" != -* ]] || refuse "ongeldige git-ref: '$DEPLOY_REF'."
if simulating; then
    note "SIMULATIEMODUS (DEPLOY_SIMULATE=1): geen root, geen runuser/chown, commando's via overrides."
    for guard in "$ENV_FILE" "$REPO_DIR" "$BACKUP_DIR" "$STAGE_ROOT" "$API_BIN_DIR"; do
        case "$guard" in
            /etc/transportationservice/*|/var/www/transportationservice*|/var/lib/transportationservice*)
                refuse "simulatie weigert productiepad '$guard' — geef sandboxpaden op (scripts/deploy-sim/)." ;;
        esac
    done
else
    [[ "$(id -u)" -eq 0 ]] || fail "dit script moet als root draaien (sudo): systemctl/nginx/back-ups vereisen root."
    require_cmd "$RUNUSER"
fi
for c in "$GIT" "$DOTNET" "$NPM" "$PSQL" "$PG_DUMP" "$PG_RESTORE" "$CURL" "$NGINX" "$RSYNC" "$SYSTEMCTL" \
         "$JOURNALCTL" "$TAR"; do
    require_cmd "$c"
done
id "$DEPLOY_USER" >/dev/null 2>&1 || fail "deploy-gebruiker '$DEPLOY_USER' bestaat niet."
[[ -d "$REPO_DIR/.git" ]] || fail "repo ontbreekt: $REPO_DIR"
[[ -r "$ENV_FILE" ]] || fail "omgevingsbestand niet leesbaar: $ENV_FILE"
[[ -x "$DOTNET_EF" ]] || fail "dotnet-ef ontbreekt op $DOTNET_EF. Installeer BUITEN een deployment als '$DEPLOY_USER': dotnet tool install --global dotnet-ef"
"$SYSTEMCTL" cat "$SERVICE_NAME" >/dev/null 2>&1 || fail "systemd-unit '$SERVICE_NAME' bestaat niet (voorbeeld: deploy/systemd/transportationservice-api.service)."
EXEC_START="$("$SYSTEMCTL" show -p ExecStart --value "$SERVICE_NAME")"
[[ "$EXEC_START" == *"$API_BIN_DIR/$API_PROJECT.dll"* ]] \
    || fail "systemd start niet uit $API_BIN_DIR — pas API_BIN_DIR/TARGET_FRAMEWORK aan. ExecStart: $EXEC_START"
SERVICE_USER="$("$SYSTEMCTL" show -p User --value "$SERVICE_NAME" 2>/dev/null || true)"
[[ -n "$SERVICE_USER" ]] || SERVICE_USER="$DEPLOY_USER"
SERVICE_WORKDIR="$("$SYSTEMCTL" show -p WorkingDirectory --value "$SERVICE_NAME" 2>/dev/null || true)"
SERVICE_WORKDIR="${SERVICE_WORKDIR#!}"; SERVICE_WORKDIR="${SERVICE_WORKDIR#-}"
mkdir -p "$BACKUP_DIR" "$STAGE_ROOT"
chmod 0750 "$BACKUP_DIR"
if command -v flock >/dev/null 2>&1; then
    exec 9>"$LOCK_FILE"
    flock -n 9 || fail "er loopt al een deployment (lock: $LOCK_FILE)."
else
    mkdir "$LOCK_FILE.d" 2>/dev/null || fail "er loopt al een deployment (lock: $LOCK_FILE.d)."
    LOCK_DIR_CREATED="$LOCK_FILE.d"
fi
cd "$REPO_DIR"
EF_VERSION="$(run_clean -- "$DOTNET_EF" --version 2>/dev/null | tail -n 1 || true)"
note "dotnet $(run_clean -- "$DOTNET" --version 2>/dev/null || echo '?'), dotnet-ef ${EF_VERSION:-?}, npm $(run_clean -- "$NPM" --version 2>/dev/null || echo '?')"
note "Gevraagde ref: $DEPLOY_REF; service-gebruiker: $SERVICE_USER; forward-only (RESTORE_PREVIOUS_ON_FAILURE=$RESTORE_PREVIOUS_ON_FAILURE)."

# =============================================================================
step "Stap 2/17: Opslag-preflight (Storage__RootPath) — wordt door dit script nooit gewijzigd"
STORAGE_ROOT="$(env_file_value Storage__RootPath)"
if [[ -z "$STORAGE_ROOT" ]]; then
    # Fallback: Environment= in the unit itself.
    STORAGE_ROOT="$("$SYSTEMCTL" show -p Environment --value "$SERVICE_NAME" 2>/dev/null | tr ' ' '\n' \
        | sed -n 's/^Storage__RootPath=//p' | tail -n 1 || true)"
fi
[[ -n "$STORAGE_ROOT" ]] \
    || refuse "Storage__RootPath ontbreekt in $ENV_FILE. Zet een absoluut pad BUITEN de release-map, bv. /var/lib/transportationservice/storage (docs/storage.md)."
[[ "$STORAGE_ROOT" == /* ]] || refuse "Storage__RootPath '$STORAGE_ROOT' is niet absoluut (relatief = afhankelijk van de werkmap = binnen de release)."
case "/$STORAGE_ROOT/" in
    */../*|*/./*) refuse "Storage__RootPath '$STORAGE_ROOT' bevat '.' of '..' — geef een genormaliseerd absoluut pad." ;;
esac
STORAGE_ROOT="$(norm_path "$STORAGE_ROOT")"
[[ "$STORAGE_ROOT" != "/" ]] || refuse "Storage__RootPath mag niet '/' zijn."
for rel in "$API_BIN_DIR" "$REPO_DIR" "$STAGE_ROOT" "$WEB_DIST_DIR" ${SERVICE_WORKDIR:+"$SERVICE_WORKDIR"}; do
    if is_within "$STORAGE_ROOT" "$rel"; then
        refuse "Storage__RootPath '$STORAGE_ROOT' ligt BINNEN '$rel'. Een deploy (rsync --delete) of opruimactie kan dan alle documenten wissen. Verplaats de opslag eerst volgens docs/storage.md."
    fi
    if is_within "$rel" "$STORAGE_ROOT"; then
        refuse "release-/werkmap '$rel' ligt binnen Storage__RootPath '$STORAGE_ROOT'. Kies een aparte opslagmap (docs/storage.md)."
    fi
done
[[ -e "$STORAGE_ROOT" ]] || refuse "Storage__RootPath '$STORAGE_ROOT' bestaat niet. Maak hem bewust aan (eigenaar $SERVICE_USER, 0750) volgens docs/storage.md; dit script maakt hem niet aan."
[[ -d "$STORAGE_ROOT" ]] || refuse "Storage__RootPath '$STORAGE_ROOT' is geen map."
PROBE_NAME=".deploy-write-probe-$$"
if ! as_service_user sh -c ': > "$1/$2" && rm -f "$1/$2"' sh "$STORAGE_ROOT" "$PROBE_NAME" 2>/dev/null; then
    refuse "Storage__RootPath '$STORAGE_ROOT' is niet schrijfbaar voor service-gebruiker '$SERVICE_USER'."
fi
if is_within "$BACKUP_DIR" "$STORAGE_ROOT" && ! is_within "$BACKUP_DIR" "$STORAGE_ROOT/backups"; then
    warn "BACKUP_DIR '$BACKUP_DIR' ligt binnen de opslag; de opslag-snapshot zou de dumps meenemen."
fi
# Legacy App_Data (pre-Storage:RootPath installs): uploads would live INSIDE the release dir.
LEGACY_DIRS="$API_BIN_DIR/App_Data"
[[ -n "$SERVICE_WORKDIR" && "$SERVICE_WORKDIR" != "$API_BIN_DIR" ]] && LEGACY_DIRS="$LEGACY_DIRS"$'\n'"$SERVICE_WORKDIR/App_Data"
for legacy in $LEGACY_DIRS; do
    [[ -d "$legacy" ]] || continue
    if [[ -n "$(find "$legacy" -type f -print 2>/dev/null | head -n 1 || true)" ]]; then
        LEGACY_COUNT="$(find "$legacy" -type f 2>/dev/null | wc -l | tr -d ' ')"
        note "Legacy-opslag gevonden: $legacy ($LEGACY_COUNT bestand(en))."
        note "Deze bestanden staan BINNEN de release-map en zouden bij een volgende deploy of opruiming verdwijnen."
        note "Migreer ze eerst naar Storage__RootPath ($STORAGE_ROOT) volgens docs/storage.md"
        note "(sectie over het migreren van een legacy App_Data-map), controleer de checksums, en deploy daarna opnieuw."
        refuse "legacy App_Data in de release-map — dit script verplaatst nooit zelf bestanden."
    fi
    note "Lege legacy-map $legacy gevonden (geen bestanden) — wordt door rsync uitgesloten, verder genegeerd."
done
note "Opslag OK: $STORAGE_ROOT (absoluut, buiten de release-mappen, schrijfbaar voor '$SERVICE_USER')."

# =============================================================================
step "Stap 3/17: Databaseverbinding (wachtwoord enkel via 0600 PGPASSFILE, nooit getoond)"
# Npgsql connection string, parsed in memory; NOT exported (child processes never see it).
CONN="$(env_file_value ConnectionStrings__DefaultConnection)"
[[ -n "$CONN" ]] || fail "ConnectionStrings__DefaultConnection ontbreekt in $ENV_FILE"
APP_ENVIRONMENT="$(env_file_value ASPNETCORE_ENVIRONMENT)"; APP_ENVIRONMENT="${APP_ENVIRONMENT:-Production}"
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
# Connection string for the migration step only: no command timeout (or the configured one)
# and a lock_timeout so a lock wait fails fast instead of hanging the downtime (H6/MIG-03).
build_migration_conn() {
    local out="" part k has_options=0
    while IFS= read -r -d ';' part || [[ -n "$part" ]]; do
        [[ -z "${part//[[:space:]]/}" ]] && continue
        k="$(printf '%s' "${part%%=*}" | tr '[:upper:]' '[:lower:]' | tr -d '[:space:]')"
        case "$k" in
            commandtimeout) continue ;;
            options) part="$part -c lock_timeout=$MIGRATION_LOCK_TIMEOUT"; has_options=1 ;;
        esac
        out="$out$part;"
    done <<<"$CONN;"
    out="${out}Command Timeout=$MIGRATION_COMMAND_TIMEOUT;"
    (( has_options == 1 )) || out="${out}Options=-c lock_timeout=$MIGRATION_LOCK_TIMEOUT;"
    printf '%s' "$out"
}
DB_HOST="$(conn_part host server || printf 'localhost')"
DB_PORT="$(conn_part port || printf '5432')"
DB_NAME="$(conn_part database || true)"; [[ -n "$DB_NAME" ]] || fail "Database ontbreekt in de connectionstring."
DB_USER="$(conn_part username userid user uid || true)"; [[ -n "$DB_USER" ]] || fail "Username ontbreekt in de connectionstring."
DB_PASS="$(conn_part password pwd || true)"
PGPASSFILE_TMP="$(mktemp "$PGPASS_DIR/.pgpass-deploy.XXXXXX")"
chmod 600 "$PGPASSFILE_TMP"
pgpass_escape() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/:/\\:/g'; }
printf '%s:%s:%s:%s:%s\n' "$(pgpass_escape "$DB_HOST")" "$DB_PORT" "$(pgpass_escape "$DB_NAME")" \
    "$(pgpass_escape "$DB_USER")" "$(pgpass_escape "$DB_PASS")" > "$PGPASSFILE_TMP"
unset DB_PASS
psql_q() {
    # lock_timeout: a read in this script must never hang behind a lock held by another session.
    PGPASSFILE="$PGPASSFILE_TMP" PGCONNECT_TIMEOUT=10 PGOPTIONS="${PSQL_PGOPTIONS:--c lock_timeout=30s}" \
        "$PSQL" -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -X -q -A -t -w -v ON_ERROR_STOP=1 "$@"
}
psql_q -c "SELECT 1;" >/dev/null || fail "database niet bereikbaar ($DB_NAME op $DB_HOST:$DB_PORT)."
SERVER_VERSION_NUM="$(psql_q -c 'SHOW server_version_num;')"
(( SERVER_VERSION_NUM >= 130000 )) || fail "PostgreSQL $SERVER_VERSION_NUM < 13: migraties gebruiken gen_random_uuid() (MIG-05c). Vereist: 16."
(( SERVER_VERSION_NUM >= 160000 )) || warn "PostgreSQL-server is ouder dan 16 ($SERVER_VERSION_NUM); de ondersteunde versie is 16."
PG_DUMP_MAJOR="$("$PG_DUMP" --version | sed -n 's/.* \([0-9][0-9]*\)\.[0-9].*/\1/p' | head -n 1)"
if [[ -n "$PG_DUMP_MAJOR" ]] && (( PG_DUMP_MAJOR * 10000 < SERVER_VERSION_NUM - SERVER_VERSION_NUM % 10000 )); then
    fail "pg_dump $PG_DUMP_MAJOR is ouder dan de server ($SERVER_VERSION_NUM); de back-up zou falen."
fi
note "Database: $DB_NAME op $DB_HOST:$DB_PORT als '$DB_USER' (server $SERVER_VERSION_NUM, ASPNETCORE_ENVIRONMENT=$APP_ENVIRONMENT)."

# Build-time / check-time values that are not secrets.
VITE_API_BASE_URL_VALUE="${VITE_API_BASE_URL:-$(env_file_value VITE_API_BASE_URL)}"
if [[ -z "$PUBLIC_URL" ]]; then PUBLIC_URL="$(env_file_value Frontend__BaseUrl)"; fi
[[ -n "$PUBLIC_URL" ]] || fail "PUBLIC_URL niet gezet en Frontend__BaseUrl ontbreekt in $ENV_FILE."
PUBLIC_URL="${PUBLIC_URL%/}"

# =============================================================================
step "Stap 4/17: Git-werkboom schoon + fetch + checkout van $DEPLOY_REF"
GIT_DIRTY="$(as_deploy "$GIT" -C "$REPO_DIR" status --porcelain --untracked-files=normal)"
if [[ -n "$GIT_DIRTY" ]]; then
    printf '%s\n' "$GIT_DIRTY" | head -n 20
    fail "werkboom in $REPO_DIR is niet schoon. Ruim bewust op (geen reset door dit script)."
fi
as_deploy "$GIT" -C "$REPO_DIR" fetch --prune origin
if as_deploy "$GIT" -C "$REPO_DIR" show-ref --verify --quiet "refs/remotes/origin/$DEPLOY_REF"; then
    as_deploy "$GIT" -C "$REPO_DIR" checkout -q "$DEPLOY_REF"
    as_deploy "$GIT" -C "$REPO_DIR" merge --ff-only -q "origin/$DEPLOY_REF"
else
    as_deploy "$GIT" -C "$REPO_DIR" checkout -q "$DEPLOY_REF"   # commit-sha, tag of lokale branch
fi
GIT_COMMIT="$(as_deploy "$GIT" -C "$REPO_DIR" rev-parse HEAD)"
SHORT_SHA="${GIT_COMMIT:0:8}"
note "Op commit $GIT_COMMIT — $(as_deploy "$GIT" -C "$REPO_DIR" log -1 --format='%s' | cut -c1-80)"
SOURCE_MIGRATIONS="$(source_migrations)"
NEWEST_SOURCE_MIGRATION="$(printf '%s\n' "$SOURCE_MIGRATIONS" | tail -n 1)"
SOURCE_MIGRATION_COUNT="$(printf '%s\n' "$SOURCE_MIGRATIONS" | grep -c . || true)"
[[ -n "$NEWEST_SOURCE_MIGRATION" ]] || fail "geen migraties gevonden in $API_PROJECT/Migrations."
note "Migraties in de broncode: $SOURCE_MIGRATION_COUNT, nieuwste: $NEWEST_SOURCE_MIGRATION"

# Schema vs. code, BEFORE any build (cheap, read-only).
HISTORY_EXISTS="$(psql_q -c "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL;")"
[[ "$HISTORY_EXISTS" == "t" ]] || fail "tabel __EFMigrationsHistory ontbreekt — dit is geen door EF beheerde productiedatabase? (nieuwe installatie: docs/operations/provisioning.md)"
APPLIED_MIGRATIONS="$(psql_q -c "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1;")"
APPLIED_COUNT="$(printf '%s\n' "$APPLIED_MIGRATIONS" | grep -c . || true)"
NEWEST_APPLIED="$(printf '%s\n' "$APPLIED_MIGRATIONS" | grep . | tail -n 1 || true)"
note "Toegepast in database: $APPLIED_COUNT migratie(s), nieuwste: ${NEWEST_APPLIED:-(geen)}"
# A schema newer than this code = a code-only rollback/downgrade. Never allowed (B2/MIG-01).
UNKNOWN_APPLIED="$(comm -23 <(printf '%s\n' "$APPLIED_MIGRATIONS" | grep . | LC_ALL=C sort || true) \
                           <(printf '%s\n' "$SOURCE_MIGRATIONS" | LC_ALL=C sort) || true)"
if [[ -n "$UNKNOWN_APPLIED" ]]; then
    note "De database bevat migraties die deze code NIET kent (schema is nieuwer dan $DEPLOY_REF):"
    printf '      %s\n' $UNKNOWN_APPLIED
    refuse "deze ref zou oude code op een nieuwer schema zetten (code-only rollback). Terugrollen kan enkel via restore: docs/delivery/operations.md §3.7."
fi
# Exact pending set (EF applies every source migration that is not in the history, also ones
# with an older timestamp from a merged branch — MIG-15).
SOURCE_PENDING="$(comm -13 <(printf '%s\n' "$APPLIED_MIGRATIONS" | grep . | LC_ALL=C sort || true) \
                          <(printf '%s\n' "$SOURCE_MIGRATIONS" | LC_ALL=C sort) || true)"
PENDING_COUNT="$(printf '%s\n' "$SOURCE_PENDING" | grep -c . || true)"
if (( PENDING_COUNT > 0 )); then
    note "Openstaande migraties ($PENDING_COUNT) — worden toegepast tijdens de downtime:"
    printf '      %s\n' $SOURCE_PENDING
    if [[ "$RESTORE_PREVIOUS_ON_FAILURE" == "1" ]]; then
        refuse "RESTORE_PREVIOUS_ON_FAILURE=1 met openstaande migraties: deze release is FORWARD-ONLY. Na een migratie mag de vorige code nooit meer starten (B2). Draai zonder RESTORE_PREVIOUS_ON_FAILURE (rollback = restore, docs/delivery/operations.md §3.7)."
    fi
else
    note "Geen openstaande migraties."
fi

# =============================================================================
step "Stap 5/17: Backend restore + Release-build naar staging (schone omgeving, geen secrets)"
STAGE_DIR="$STAGE_ROOT/stage-$(date '+%Y%m%d-%H%M%S')-$SHORT_SHA"
STAGE_BUILD="$STAGE_DIR/build/"                          # MSBuild BaseOutputPath (trailing /)
STAGE_API="${STAGE_BUILD}Release/$TARGET_FRAMEWORK"     # = what dotnet-ef --no-build will load
STAGE_WEB="$STAGE_DIR/web"
install_dir "$STAGE_DIR" "$STAGE_DIR/build" "$STAGE_WEB"
run_clean -- "$DOTNET" restore "$API_PROJECT/$API_PROJECT.csproj"
# SourceRevisionId stempelt de commit in de InformationalVersion ("0.2.0+<sha>").
run_clean "BaseOutputPath=$STAGE_BUILD" -- "$DOTNET" build "$API_PROJECT/$API_PROJECT.csproj" -c Release --no-restore \
    -p:SourceRevisionId="$SHORT_SHA"
[[ -f "$STAGE_API/$API_PROJECT.dll" ]] || fail "build leverde geen $API_PROJECT.dll op in $STAGE_API"
APP_VERSION="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' Directory.Build.props | head -n 1)"
APP_VERSION="${APP_VERSION:-0.0.0}"
cat > "$STAGE_API/deployment.json" <<METADATA
{
  "version": "$APP_VERSION",
  "commit": "$SHORT_SHA",
  "ref": "$DEPLOY_REF",
  "deployedAtUtc": "$(date -u '+%Y-%m-%dT%H:%M:%SZ')",
  "environment": "$APP_ENVIRONMENT"
}
METADATA
simulating || chown "$DEPLOY_USER:$DEPLOY_USER" "$STAGE_API/deployment.json"
note "Backend gebouwd in $STAGE_API"

# =============================================================================
step "Stap 6/17: Frontend install + productiebuild naar staging (alleen VITE_API_BASE_URL)"
if [[ -z "$VITE_API_BASE_URL_VALUE" ]] && ! grep -qs '^VITE_API_BASE_URL=' "$WEB_PROJECT/.env.production"; then
    fail "VITE_API_BASE_URL is niet gezet (deploy-omgeving, $ENV_FILE of $WEB_PROJECT/.env.production) — de bundle zou naar de dev-API wijzen."
fi
WEB_ENV="VITE_BUILD_COMMIT=$SHORT_SHA"
cd "$REPO_DIR/$WEB_PROJECT"
if [[ -n "$VITE_API_BASE_URL_VALUE" ]]; then
    run_clean "$WEB_ENV" "VITE_API_BASE_URL=$VITE_API_BASE_URL_VALUE" -- "$NPM" ci --no-audit --no-fund
    run_clean "$WEB_ENV" "VITE_API_BASE_URL=$VITE_API_BASE_URL_VALUE" -- "$NPM" run build -- --outDir "$STAGE_WEB" --emptyOutDir
else
    run_clean "$WEB_ENV" -- "$NPM" ci --no-audit --no-fund
    run_clean "$WEB_ENV" -- "$NPM" run build -- --outDir "$STAGE_WEB" --emptyOutDir
fi
cd "$REPO_DIR"
[[ -f "$STAGE_WEB/index.html" ]] || fail "frontend-build leverde geen index.html op in $STAGE_WEB"
note "Frontend gebouwd in $STAGE_WEB"

# =============================================================================
step "Stap 7/17: nginx-configuratie testen"
"$NGINX" -t

# =============================================================================
step "Stap 8/17: Database-preflight (eigenaarschap, rechten, master-data, openstaande migraties volgens EF)"
# Eigenaarschap: elke public-tabel/-sequence moet van de migratiegebruiker zijn (incident: owner 'postgres').
FOREIGN_OWNED="$(psql_q -c "
    SELECT c.relkind::text || ' ' || c.relname || ' (eigenaar: ' || pg_get_userbyid(c.relowner) || ')'
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

# Master-data/wagenpark-identiteit: de migraties breken BEWUST af op dubbels en voegen nooit zelf
# samen. Read-only preflight: stop hier, vóór de downtime (docs/delivery/operations.md §3.6).
IDENTITY_CONFLICTS="$(psql_q -F $'\t' -f "$REPO_DIR/scripts/preflight-master-data.sql")"
if [[ -n "$IDENTITY_CONFLICTS" ]]; then
    note "Master-data-conflicten (soort, tenant, genormaliseerde waarde, aantal, rijen):"
    printf '%s\n' "$IDENTITY_CONFLICTS"
    note "Los ze eerst manueel op volgens docs/delivery/operations.md §3.6; er wordt niets automatisch samengevoegd."
    fail "master-data-preflight mislukt."
fi
note "Master-data-preflight OK (klantnummers, e-mailadressen, rolnamen, kentekens/VIN, sjabloonaantallen)."

# Pending set according to EF, against the STAGED build (exactly the binaries that will run).
ef_clean() {
    local conn="$1"; shift
    run_clean "BaseOutputPath=$STAGE_BUILD" "ASPNETCORE_ENVIRONMENT=$APP_ENVIRONMENT" \
        "ConnectionStrings__DefaultConnection=$conn" -- "$DOTNET_EF" "$@" \
        --project "$API_PROJECT" --startup-project "$API_PROJECT" \
        --configuration Release --no-build --no-color --prefix-output
}
ef_list() {  # read-only; one retry for a transient tool/MSBuild hiccup, full output kept in the stage dir
    local out attempt
    for attempt in 1 2; do
        if out="$(ef_clean "$CONN" migrations list 2>&1)"; then
            printf '%s\n' "$out"
            return 0
        fi
        printf '%s\n' "$out" > "$STAGE_DIR/ef-list-attempt$attempt.log"
        note "dotnet ef migrations list faalde (poging $attempt, volledige uitvoer: $STAGE_DIR/ef-list-attempt$attempt.log)." >&2
        sleep 3
    done
    printf '%s\n' "$out" | grep -v 'warning NU19' | tail -n 30 >&2
    return 1
}
MIGRATIONS_LIST="$(ef_list)" || fail "dotnet ef migrations list mislukt."
grep -q "$NEWEST_SOURCE_MIGRATION" <<<"$MIGRATIONS_LIST" \
    || fail "gestagede assembly kent migratie $NEWEST_SOURCE_MIGRATION niet — build komt niet overeen met de code."
if grep -qiE 'unable to connect|could not connect|failed to connect' <<<"$MIGRATIONS_LIST"; then
    fail "dotnet-ef kon de database niet bereiken; pending-status onbekend."
fi
EF_PENDING="$(grep -E '\(Pending\)' <<<"$MIGRATIONS_LIST" | sed -E -e 's/^[^:]*:[[:space:]]*//' -e 's/[[:space:]]*\(Pending\)//' || true)"
EF_PENDING_COUNT="$(printf '%s\n' "$EF_PENDING" | grep -c . || true)"
[[ "$EF_PENDING_COUNT" == "$PENDING_COUNT" ]] \
    || fail "EF meldt $EF_PENDING_COUNT openstaande migraties, de historiek-vergelijking $PENDING_COUNT — onderzoek vóór de downtime."
note "EF bevestigt $EF_PENDING_COUNT openstaande migratie(s) tegen de gestagede build."

# =============================================================================
# Vanaf hier ontstaat downtime: onderhoud aan → stop → finale dump + opslag-snapshot →
# migreren → valideren → binaries plaatsen → start → frontend → onderhoud uit.
# =============================================================================
step "Stap 9/17: Onderhoudspagina aan + service stoppen ($SERVICE_NAME) — downtime start"
if service_active; then SERVICE_WAS_ACTIVE=1; fi
DOWNTIME_STARTED=1
maintenance_on
[[ "$MAINTENANCE_MODE" == "1" ]] && note "Onderhoudsvlag gezet: $MAINTENANCE_FLAG"
"$SYSTEMCTL" stop "$SERVICE_NAME"
SERVICE_STOPPED=1
service_active && fail "service draait nog na stop."
note "Service gestopt (was actief: $SERVICE_WAS_ACTIVE). Vanaf nu schrijft de applicatie niets meer."

# =============================================================================
step "Stap 10/17: Finale databaseback-up + verificatie + opslag-snapshot"
BACKUP_STAMP="$(date '+%Y%m%d-%H%M%S')"
BACKUP_PATH="$BACKUP_DIR/${DB_NAME}-${BACKUP_STAMP}.dump"
BACKUP_TIME="$(ts)"
if ! PGPASSFILE="$PGPASSFILE_TMP" "$PG_DUMP" -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -w -d "$DB_NAME" -Fc \
        -f "$BACKUP_PATH" 2> "$BACKUP_PATH.log"; then
    cat "$BACKUP_PATH.log" >&2
    fail "pg_dump mislukt (log: $BACKUP_PATH.log)."
fi
chmod 600 "$BACKUP_PATH"
[[ -s "$BACKUP_PATH" ]] || fail "back-up is leeg: $BACKUP_PATH"
"$PG_RESTORE" --list "$BACKUP_PATH" > "$BACKUP_PATH.toc" 2>> "$BACKUP_PATH.log" \
    || fail "pg_restore --list kan de dump niet lezen: $BACKUP_PATH"
grep -q 'TABLE DATA public __EFMigrationsHistory' "$BACKUP_PATH.toc" \
    || fail "dump bevat geen data van __EFMigrationsHistory — dump onvolledig: $BACKUP_PATH"
note "Back-up: $BACKUP_PATH ($(du -h "$BACKUP_PATH" | cut -f1), $(grep -c '^[0-9]' "$BACKUP_PATH.toc") TOC-items, genomen $BACKUP_TIME)"

CHECKSUM_TARGETS="$(basename "$BACKUP_PATH")"
if [[ "$STORAGE_SNAPSHOT" == "1" ]]; then
    STORAGE_SNAPSHOT_PATH="$BACKUP_DIR/storage-${DB_NAME}-${BACKUP_STAMP}.tar.gz"
    # backups/ (in-app DB dumps, already covered) and message-sink/ (dev only) are excluded.
    "$TAR" -C "$STORAGE_ROOT" -czf "$STORAGE_SNAPSHOT_PATH" --exclude=./backups --exclude=./message-sink . \
        || fail "opslag-snapshot mislukt: $STORAGE_SNAPSHOT_PATH"
    chmod 600 "$STORAGE_SNAPSHOT_PATH"
    "$TAR" -tzf "$STORAGE_SNAPSHOT_PATH" >/dev/null || fail "opslag-snapshot onleesbaar: $STORAGE_SNAPSHOT_PATH"
    CHECKSUM_TARGETS="$CHECKSUM_TARGETS"$'\n'"$(basename "$STORAGE_SNAPSHOT_PATH")"
    note "Opslag-snapshot: $STORAGE_SNAPSHOT_PATH ($(du -h "$STORAGE_SNAPSHOT_PATH" | cut -f1))"
else
    STORAGE_SNAPSHOT_PATH="(overgeslagen: STORAGE_SNAPSHOT=0 — zorg voor een gelijktijdige off-host bestandsback-up)"
    warn "opslag-snapshot overgeslagen (STORAGE_SNAPSHOT=0)."
fi
CHECKSUM_PATH="$BACKUP_DIR/${DB_NAME}-${BACKUP_STAMP}.sha256"
( cd "$BACKUP_DIR" && sha256_files $CHECKSUM_TARGETS > "$CHECKSUM_PATH" )
chmod 600 "$CHECKSUM_PATH"
note "Checksums: $CHECKSUM_PATH (kopieer dump + snapshot + checksums off-host)."

# =============================================================================
step "Stap 11/17: EF Core-migraties toepassen (gestagede build, lock_timeout=$MIGRATION_LOCK_TIMEOUT, Command Timeout=$MIGRATION_COMMAND_TIMEOUT)"
MIGRATION_LOG="$STAGE_DIR/migration.log"
: > "$MIGRATION_LOG"
if (( PENDING_COUNT > 0 )); then
    MIGRATION_CONN="$(build_migration_conn)"
    MIGRATION_RESULT="MISLUKT of onderbroken (zie foutmelding hierboven; EF commit per migratie)"
    MIGRATION_STARTED=1
    ef_update() {
        if [[ "$MIGRATION_VERBOSE" == "1" ]]; then ef_clean "$MIGRATION_CONN" database update --verbose
        else ef_clean "$MIGRATION_CONN" database update; fi
    }
    set +e
    ef_update 2>&1 | while IFS= read -r line; do
        printf '%s\t%s\n' "$(date '+%s')" "$line" >> "$MIGRATION_LOG"
        printf '      %s\n' "$line"
    done
    EF_STATUS="${PIPESTATUS[0]}"
    set -e
    unset MIGRATION_CONN
    # Per-migration duration (seconds) from the timestamped EF output.
    MIGRATION_TIMINGS="$(awk -F '\t' '
        /Applying migration/ { if (cur != "") printf "%s\t%ds\n", cur, $1 - start;
                               cur = $2; sub(/.*Applying migration .?/, "", cur); sub(/.\.?[[:space:]]*$/, "", cur); start = $1 }
        { last = $1 }
        END { if (cur != "") printf "%s\t%ds%s\n", cur, last - start, (status == 0 ? "" : " (MISLUKT)") }
    ' status="$EF_STATUS" "$MIGRATION_LOG")"
    if [[ -n "$MIGRATION_TIMINGS" ]]; then
        note "Duur per migratie:"
        printf '%s\n' "$MIGRATION_TIMINGS" | tr '\t' ' ' | sed 's/^/      /'
        printf '%s\n' "$MIGRATION_TIMINGS" > "$STAGE_DIR/migration-timings.tsv"
    fi
    if (( EF_STATUS != 0 )); then
        if grep -qE '55P03|lock timeout|canceling statement due to lock timeout' "$MIGRATION_LOG"; then
            note "Oorzaak: lock_timeout ($MIGRATION_LOCK_TIMEOUT) — een andere sessie hield een lock (pg_stat_activity)."
        fi
        fail "dotnet ef database update mislukt (exitcode $EF_STATUS)."
    fi
    MIGRATION_RESULT="toegepast: $PENDING_COUNT migratie(s)"
else
    MIGRATION_RESULT="geen openstaande migraties"
fi

# =============================================================================
step "Stap 12/17: Migratiestand valideren (0 openstaand, historiek == broncode)"
MIGRATIONS_LIST_AFTER="$(ef_list)" || fail "dotnet ef migrations list (na migreren) mislukt."
if grep -qE '\(Pending\)' <<<"$MIGRATIONS_LIST_AFTER"; then
    grep -E '\(Pending\)' <<<"$MIGRATIONS_LIST_AFTER" >&2
    fail "na migreren meldt EF nog openstaande migraties."
fi
NEWEST_APPLIED_AFTER="$(psql_q -c "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1 DESC LIMIT 1;")"
APPLIED_COUNT_AFTER="$(psql_q -c "SELECT count(*) FROM \"__EFMigrationsHistory\";")"
[[ "$NEWEST_APPLIED_AFTER" == "$NEWEST_SOURCE_MIGRATION" ]] \
    || fail "nieuwste toegepaste migratie is '$NEWEST_APPLIED_AFTER', verwacht '$NEWEST_SOURCE_MIGRATION'."
[[ "$APPLIED_COUNT_AFTER" == "$SOURCE_MIGRATION_COUNT" ]] \
    || fail "historiek telt $APPLIED_COUNT_AFTER migraties, broncode $SOURCE_MIGRATION_COUNT."
note "Migratieresultaat: $MIGRATION_RESULT — schema staat op $NEWEST_APPLIED_AFTER ($APPLIED_COUNT_AFTER)."

# =============================================================================
step "Stap 13/17: Vorige binaries bewaren en nieuwe binaries plaatsen (service is gestopt)"
service_active && fail "service is onverwacht actief vóór het wisselen van binaries."
# App_Data/ is ALWAYS excluded: legacy installs kept uploads there (production-readiness B1).
if [[ -d "$API_BIN_DIR" ]]; then
    PREV_BIN_DIR="$STAGE_DIR/previous-api"
    install_dir "$PREV_BIN_DIR"
    "$RSYNC" -a --exclude=/App_Data/ "$API_BIN_DIR/" "$PREV_BIN_DIR/"
    note "Vorige binaries bewaard in $PREV_BIN_DIR"
fi
install_dir "$API_BIN_DIR"
BINARIES_INSTALL_STARTED=1
rsync_owned -a --delete --exclude=/App_Data/ "$STAGE_API/" "$API_BIN_DIR/"
note "Nieuwe binaries staan in $API_BIN_DIR."

# =============================================================================
step "Stap 14/17: Service starten en wachten op $LOCAL_API_URL$READY_PATH (max ${STARTUP_WAIT_SECONDS}s)"
RESTARTS_BEFORE="$("$SYSTEMCTL" show -p NRestarts --value "$SERVICE_NAME" 2>/dev/null || echo 0)"
[[ "$RESTARTS_BEFORE" =~ ^[0-9]+$ ]] || RESTARTS_BEFORE=0
NEW_BINARIES_STARTED=1
"$SYSTEMCTL" start "$SERVICE_NAME"
READY_CODE="000"
READY_DEADLINE=$((SECONDS + STARTUP_WAIT_SECONDS))
while (( SECONDS < READY_DEADLINE )); do
    if "$SYSTEMCTL" is-failed --quiet "$SERVICE_NAME"; then
        show_service_logs
        fail "service is in 'failed' na de start (startupvalidatie of seeder faalde)."
    fi
    RESTARTS_NOW="$("$SYSTEMCTL" show -p NRestarts --value "$SERVICE_NAME" 2>/dev/null || echo 0)"
    [[ "$RESTARTS_NOW" =~ ^[0-9]+$ ]] || RESTARTS_NOW=0
    if (( RESTARTS_NOW - RESTARTS_BEFORE >= MAX_START_RESTARTS )); then
        show_service_logs
        fail "service herstartte $((RESTARTS_NOW - RESTARTS_BEFORE))x tijdens het opstarten (crashloop)."
    fi
    if service_active; then
        READY_CODE="$("$CURL" -s -o /dev/null -m 5 -w '%{http_code}' "$LOCAL_API_URL$READY_PATH" || true)"
        [[ "$READY_CODE" == "200" ]] && break
    fi
    sleep 2
done
if [[ "$READY_CODE" != "200" ]]; then
    READY_BODY="$("$CURL" -s -m 5 "$LOCAL_API_URL$READY_PATH" 2>/dev/null | head -c 600 || true)"
    [[ -n "$READY_BODY" ]] && note "Laatste antwoord van $READY_PATH: $READY_BODY"
    show_service_logs
    fail "API werd niet ready binnen ${STARTUP_WAIT_SECONDS}s (laatste status: $READY_CODE)."
fi
HEALTH_RESULT="ready (GET $READY_PATH = 200)"
note "Service actief en $HEALTH_RESULT ($("$SYSTEMCTL" show -p MainPID --value "$SERVICE_NAME" | sed 's/^/PID /'))."

# =============================================================================
step "Stap 15/17: Frontend live zetten (vorige dist bewaard) + nginx herladen + onderhoud uit"
PREV_WEB_DIR="$STAGE_DIR/previous-web"
install_dir "$PREV_WEB_DIR" "$WEB_DIST_DIR"
"$RSYNC" -a "$WEB_DIST_DIR/" "$PREV_WEB_DIR/"
rsync_owned -a --delete "$STAGE_WEB/" "$WEB_DIST_DIR/"
"$NGINX" -t
"$SYSTEMCTL" reload nginx
maintenance_off
RELEASE_LIVE=1
note "Frontend staat in $WEB_DIST_DIR (vorige in $PREV_WEB_DIR); nginx herladen; onderhoudspagina uit."

# =============================================================================
step "Stap 16/17: Publieke website-check ($PUBLIC_URL)"
PUBLIC_CODE="000"
for ((i = 0; i < 10; i++)); do
    PUBLIC_CODE="$("$CURL" -s -o /dev/null -m 10 -L -w '%{http_code}' "$PUBLIC_URL/" || true)"
    [[ "$PUBLIC_CODE" == "200" ]] && break
    sleep 2
done
[[ "$PUBLIC_CODE" == "200" ]] || fail "publieke site geeft HTTP $PUBLIC_CODE (verwacht 200) — release is wel live."
note "Publieke site: HTTP 200."

# =============================================================================
step "Stap 17/17: Opruimen (oude back-ups, snapshots en staging)"
prune_newest() {  # prune_newest <keep> <glob...> — keeps the newest <keep> files (by mtime)
    local keep="$1"; shift
    local n=0 f
    for f in $(ls -1t "$@" 2>/dev/null || true); do
        n=$((n + 1))
        (( n > keep )) || continue
        rm -f -- "$f" "$f.toc" "$f.log"
        note "Verwijderd (retentie): $f"
    done
}
prune_newest "$KEEP_BACKUPS" "$BACKUP_DIR/$DB_NAME"-*.dump
prune_newest "$KEEP_BACKUPS" "$BACKUP_DIR/$DB_NAME"-*.sha256
prune_newest "$KEEP_STORAGE_SNAPSHOTS" "$BACKUP_DIR/storage-$DB_NAME"-*.tar.gz
# Staging: newest $KEEP_STAGES stay (they hold previous-api/previous-web for a restore-based rollback).
n=0
for d in $(ls -1dt "$STAGE_ROOT"/stage-* 2>/dev/null || true); do
    n=$((n + 1))
    (( n > KEEP_STAGES )) || continue
    [[ -d "$d" && "$d" != "$STAGE_DIR" ]] && rm -rf -- "$d"
done
note "Back-ups bewaard: $(ls -1 "$BACKUP_DIR/$DB_NAME"-*.dump 2>/dev/null | wc -l | tr -d ' ') (max $KEEP_BACKUPS)."

DEPLOY_DONE=1
cat <<SUMMARY

================= DEPLOYMENT GESLAAGD =================
Tijdstip          : $(ts)
Domein            : $PUBLIC_URL  (HTTP $PUBLIC_CODE)
Git-commit        : $GIT_COMMIT ($DEPLOY_REF)
Versie            : $APP_VERSION+$SHORT_SHA
Databaseback-up   : $BACKUP_PATH (genomen $BACKUP_TIME, service gestopt)
Opslag-snapshot   : $STORAGE_SNAPSHOT_PATH
Checksums         : $CHECKSUM_PATH
Migraties         : $MIGRATION_RESULT
Schema            : $NEWEST_APPLIED_AFTER
Service           : $SERVICE_NAME — actief, $HEALTH_RESULT
Vorige binaries   : ${PREV_BIN_DIR:-(geen)}
Vorige frontend   : ${PREV_WEB_DIR:-(geen)}
Opslag            : $STORAGE_ROOT (niet aangeraakt)
=======================================================
Volgende: AFTER DEPLOY-checks (docs/delivery/operations.md §6), VACUUM (ANALYZE) na grote
migraties, en kopieer dump + snapshot + checksums off-host.
SUMMARY
