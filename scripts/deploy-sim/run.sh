#!/bin/bash
#
# scripts/deploy-sim/run.sh — local deploy SIMULATION for scripts/deploy-transportationservice.sh.
#
# Runs the REAL deploy script (DEPLOY_SIMULATE=1, bash 3.2-compatible, no root) against a fake
# server layout in a temp dir, with:
#   * a real PostgreSQL 16 cluster of its own (initdb, 127.0.0.1:$SIM_PG_PORT, no unix socket),
#   * the real dotnet SDK + real dotnet-ef (wrapped to record what they receive),
#   * stubs for systemctl/journalctl/nginx/npm that record every call in state/events.log,
#   * a throw-away git clone of this worktree (origin = this worktree) as REPO_DIR.
# Nothing outside the temp dir is written, no root is used, no real server is contacted.
#
# Usage:
#   scripts/deploy-sim/run.sh [--keep|--cleanup] [scenario ...]
#   scenarios: core (default) | app | all | <name> ...
#     core: no-ref storage-unset storage-relative storage-missing storage-unwritable
#           storage-inside-release legacy-app-data restore-previous-pending success
#           startup-never-ready startup-crash lock-timeout migration-failure db-ahead-of-code
#     app (need the integrated app: GET /health/ready + Storage:RootPath support; run by the
#          coordinator after integration): app-real-success app-missing-encryption-key
#          app-db-down-at-start
#   Knobs for extra app-level scenarios: SIM_ENV_EXTRA (newline-separated KEY=VALUE appended to
#   tms.env), SIM_ENV_OMIT (space-separated keys removed), SIM_PRE_DEPLOY_SQL (file run as
#   superuser on the DB before the deploy) — e.g. a Peppol 'Live' row or an unreachable SMTP host.
#
# Requirements (macOS defaults): PGBIN=/opt/homebrew/opt/postgresql@16/bin,
#   REAL_DOTNET=/usr/local/share/dotnet/dotnet, REAL_DOTNET_EF=~/.dotnet/tools/dotnet-ef,
#   python3, rsync, git. Ports: SIM_PG_PORT=5470, SIM_API_PORT=5471, SIM_WEB_PORT=5472.
#
set -u
IFS=$'\n\t'

HERE="$(cd "$(dirname "$0")" && pwd)"
WORKTREE="$(cd "$HERE/../.." && pwd)"
DEPLOY_SCRIPT="$WORKTREE/scripts/deploy-transportationservice.sh"
PGBIN="${PGBIN:-/opt/homebrew/opt/postgresql@16/bin}"
REAL_DOTNET="${REAL_DOTNET:-/usr/local/share/dotnet/dotnet}"
REAL_DOTNET_ROOT="$(dirname "$REAL_DOTNET")"
REAL_DOTNET_EF="${REAL_DOTNET_EF:-$HOME/.dotnet/tools/dotnet-ef}"
SIM_PG_PORT="${SIM_PG_PORT:-5470}"
SIM_API_PORT="${SIM_API_PORT:-5471}"
SIM_WEB_PORT="${SIM_WEB_PORT:-5472}"
SIM_ROOT="${SIM_ROOT:-$(mktemp -d /tmp/tms-deploy-sim.XXXXXX)}"
mkdir -p "$SIM_ROOT" || exit 1
SIM_ROOT="$(cd "$SIM_ROOT" && pwd -P)" || exit 1
if [[ -z "$SIM_ROOT" || "$SIM_ROOT" == "/" || -n "$(ls -A "$SIM_ROOT")" ]]; then
    echo "deploy-sim: SIM_ROOT must be a new or empty directory (got '$SIM_ROOT')" >&2
    exit 1
fi
KEEP=1

SCENARIOS=()
for a in "$@"; do
    case "$a" in
        --keep) KEEP=1 ;;
        --cleanup) KEEP=0 ;;
        *) SCENARIOS+=("$a") ;;
    esac
done
CORE="no-ref storage-unset storage-relative storage-missing storage-unwritable storage-inside-release legacy-app-data restore-previous-pending success startup-never-ready startup-crash lock-timeout migration-failure db-ahead-of-code"
APP="app-real-success app-missing-encryption-key app-db-down-at-start app-bad-frontend-url app-peppol-live"
if [[ ${#SCENARIOS[@]} -eq 0 ]]; then SCENARIOS=(core); fi
EXPANDED=""
for s in "${SCENARIOS[@]}"; do
    case "$s" in
        core) EXPANDED="$EXPANDED $CORE" ;;
        app) EXPANDED="$EXPANDED $APP" ;;
        all) EXPANDED="$EXPANDED $CORE $APP" ;;
        *) EXPANDED="$EXPANDED $s" ;;
    esac
done

BIN="$SIM_ROOT/bin"
STATE="$SIM_ROOT/state"
LOGS="$SIM_ROOT/logs"
SRV="$SIM_ROOT/srv"
REPO="$SRV/repo"
ENV_FILE="$SRV/etc/tms.env"
STORAGE="$SRV/storage"
BACKUPS="$SRV/backups"
RELEASES="$SRV/releases"
MAINT_FLAG="$SRV/maintenance/maintenance.on"
API_BIN_DIR="$REPO/TransportationService.Api/bin/Release/net10.0"
WEB_DIST_DIR="$REPO/TransportationService.Web/dist"
PGDATA="$SIM_ROOT/pg"
SIM_EVENTS="$STATE/events.log"
PSQL_SU=("$PGBIN/psql" -h 127.0.0.1 -p "$SIM_PG_PORT" -U postgres -X -q -A -t -v ON_ERROR_STOP=1)
WEB_PID=""
TOTAL_PASS=0; TOTAL_FAIL=0
SUMMARY=""

say() { printf '[sim %s] %s\n' "$(date '+%H:%M:%S')" "$*"; }
die() { say "FATAL: $*"; teardown; exit 1; }

# ---------------------------------------------------------------- setup ----
check_prereqs() {
    [[ -x "$PGBIN/initdb" ]] || die "PostgreSQL 16 not found in $PGBIN"
    [[ -x "$REAL_DOTNET" ]] || die "dotnet not found at $REAL_DOTNET"
    [[ -x "$REAL_DOTNET_EF" ]] || die "dotnet-ef not found at $REAL_DOTNET_EF"
    command -v python3 >/dev/null || die "python3 required"
    command -v rsync >/dev/null || die "rsync required"
    local p
    for p in "$SIM_PG_PORT" "$SIM_API_PORT" "$SIM_WEB_PORT"; do
        if nc -z 127.0.0.1 "$p" 2>/dev/null; then die "port $p is already in use"; fi
    done
}

rand_hex() { python3 -c "import secrets;print(secrets.token_hex($1))"; }
rand_b64_32() { python3 -c "import secrets,base64;print(base64.b64encode(secrets.token_bytes(32)).decode())"; }

setup_dirs() {
    mkdir -p "$BIN" "$STATE" "$LOGS" "$SRV/etc" "$SRV/maintenance" "$SIM_ROOT/run" "$SIM_ROOT/home"
    cp "$HERE"/stubs/* "$BIN/"
    chmod +x "$BIN"/*
    cat > "$BIN/sim.conf" <<CONF
REAL_DOTNET="$REAL_DOTNET"
REAL_DOTNET_ROOT="$REAL_DOTNET_ROOT"
REAL_DOTNET_EF="$REAL_DOTNET_EF"
PGBIN="$PGBIN"
CONF
    DB_PASSWORD="sim-$(rand_hex 12)"
    JWT_KEY="$(rand_hex 48)"
    COL_KEY="$(rand_b64_32)"
    SMTP_PASSWORD="sim-$(rand_hex 8)"
}

setup_pg() {
    say "initdb + start PostgreSQL on 127.0.0.1:$SIM_PG_PORT"
    "$PGBIN/initdb" -D "$PGDATA" -U postgres -E UTF8 --locale=C --auth-local=trust --auth-host=scram-sha-256 \
        > "$LOGS/initdb.log" 2>&1 || die "initdb failed"
    cat >> "$PGDATA/postgresql.conf" <<PGCONF
port = $SIM_PG_PORT
listen_addresses = '127.0.0.1'
unix_socket_directories = ''
fsync = off
PGCONF
    cat > "$PGDATA/pg_hba.conf" <<HBA
host all postgres 127.0.0.1/32 trust
host all all 127.0.0.1/32 scram-sha-256
HBA
    "$PGBIN/pg_ctl" -D "$PGDATA" -l "$LOGS/postgres.log" -w start >/dev/null || die "pg_ctl start failed"
    "${PSQL_SU[@]}" -d postgres -c "CREATE ROLE tms LOGIN PASSWORD '$DB_PASSWORD';" || die "create role"
    "${PSQL_SU[@]}" -d postgres -c "CREATE DATABASE tms_base OWNER tms;" || die "create db"
}

setup_repo() {
    say "cloning $WORKTREE -> $REPO"
    git clone -q "$WORKTREE" "$REPO" || die "git clone failed"
    SIM_REF="${SIM_REF:-$(git -C "$WORKTREE" rev-parse HEAD)}"
    git -C "$REPO" checkout -q --detach "$SIM_REF" || die "checkout $SIM_REF failed"
    SHORT_REF="${SIM_REF:0:8}"
    SOURCE_MIGS="$(find "$REPO/TransportationService.Api/Migrations" -maxdepth 1 -name '[0-9]*_*.cs' ! -name '*.Designer.cs' \
        | sed -e 's#.*/##' -e 's/\.cs$//' | sort)"
    SOURCE_COUNT="$(printf '%s\n' "$SOURCE_MIGS" | grep -c .)"
    NEWEST_MIG="$(printf '%s\n' "$SOURCE_MIGS" | tail -n 1)"
    BASE_TARGET="$(printf '%s\n' "$SOURCE_MIGS" | tail -n 4 | head -n 1)"
    BASE_COUNT=$((SOURCE_COUNT - 3))
    # Test-only failing migration on a LOCAL branch of the throw-away clone.
    git -C "$REPO" checkout -q -b sim/forced-failure "$SIM_REF"
    cp "$HERE/fixtures/SimForcedFailure.cs.txt" "$REPO/TransportationService.Api/Migrations/29990101000000_SimForcedFailure.cs"
    git -C "$REPO" add TransportationService.Api/Migrations/29990101000000_SimForcedFailure.cs
    git -C "$REPO" -c user.name=deploy-sim -c user.email=deploy-sim@localhost commit -q -m "sim: forced failing migration (test-only)"
    git -C "$REPO" checkout -q --detach "$SIM_REF"
    # An older ref that does not know the newest migration (for db-ahead-of-code).
    local added_in
    added_in="$(git -C "$REPO" log -1 --format=%H -- "TransportationService.Api/Migrations/$NEWEST_MIG.cs")"
    OLD_REF="$(git -C "$REPO" rev-parse "$added_in^")"
    say "ref $SIM_REF; $SOURCE_COUNT migrations, base = $BASE_TARGET ($BASE_COUNT applied, 3 pending); old ref $OLD_REF"
}

app_conn() { printf 'Host=127.0.0.1;Port=%s;Database=%s;Username=tms;Password=%s' "$SIM_PG_PORT" "$1" "$DB_PASSWORD"; }

setup_base_db() {
    say "harness build (same SourceRevisionId as the deploy, so the deploy build is incremental)"
    ( cd "$REPO" && BaseOutputPath="$SIM_ROOT/harness-build/" DOTNET_ROOT="$REAL_DOTNET_ROOT" DOTNET_NOLOGO=1 \
        "$REAL_DOTNET" build TransportationService.Api/TransportationService.Api.csproj -c Release \
        -p:SourceRevisionId="$SHORT_REF" ) > "$LOGS/harness-build.log" 2>&1 || die "harness build failed (see $LOGS/harness-build.log)"
    say "migrating tms_base to $BASE_TARGET"
    ef_harness tms_base database update "$BASE_TARGET" > "$LOGS/base-db.log" 2>&1 || die "base migration failed (see $LOGS/base-db.log)"
}

# ef_harness <db> <ef args...> — real dotnet-ef against the harness build.
ef_harness() {
    local db="$1"; shift
    ( cd "$REPO" && export BaseOutputPath="$SIM_ROOT/harness-build/" DOTNET_ROOT="$REAL_DOTNET_ROOT" \
        PATH="$REAL_DOTNET_ROOT:$PATH" ASPNETCORE_ENVIRONMENT=Production \
        ConnectionStrings__DefaultConnection="$(app_conn "$db")" && \
      "$REAL_DOTNET_EF" "$@" --project TransportationService.Api --startup-project TransportationService.Api \
        --configuration Release --no-build --no-color )
}

start_web() {
    mkdir -p "$WEB_DIST_DIR"
    nohup python3 -m http.server "$SIM_WEB_PORT" --bind 127.0.0.1 --directory "$WEB_DIST_DIR" \
        > "$LOGS/public-web.log" 2>&1 &
    WEB_PID=$!
}

teardown() {
    [[ -x "$BIN/systemctl" ]] && SIM_ROOT="$SIM_ROOT" API_BIN_DIR="$API_BIN_DIR" "$BIN/systemctl" stop transportationservice-api >/dev/null 2>&1
    [[ -n "$WEB_PID" ]] && kill "$WEB_PID" 2>/dev/null
    kill_locker
    [[ -f "$PGDATA/postmaster.pid" ]] && "$PGBIN/pg_ctl" -D "$PGDATA" -m fast -w stop >/dev/null 2>&1
    if [[ "$KEEP" == "0" ]]; then
        rm -rf "$SIM_ROOT/pg" "$SRV" "$SIM_ROOT/harness-build"
        say "cleaned up (logs kept in $LOGS)"
    fi
}

kill_locker() {
    if [[ -f "$STATE/locker.pid" ]]; then kill "$(cat "$STATE/locker.pid")" 2>/dev/null; rm -f "$STATE/locker.pid"; fi
    if [[ -f "$PGDATA/postmaster.pid" ]]; then
        "${PSQL_SU[@]}" -d postgres -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name='deploy-sim-locker';" >/dev/null 2>&1
    fi
}

# -------------------------------------------------------------- fixture ----
write_env() {
    {
        echo "# deploy-sim tms.env — sim-only random values, generated per run"
        echo "ASPNETCORE_ENVIRONMENT=Production"
        echo "ConnectionStrings__DefaultConnection=$(app_conn tms_sim)"
        echo "Jwt__SigningKey=$JWT_KEY"
        echo "ColumnEncryption__Key=$COL_KEY"
        echo "Email__Smtp__Host=127.0.0.1"
        echo "Email__Smtp__Port=2525"
        echo "Email__Smtp__Username=sim"
        echo "Email__Smtp__Password=$SMTP_PASSWORD"
        echo "Email__Smtp__FromAddress=noreply@sim.invalid"
        echo "Email__Smtp__FromName=\"TMS sim\""
        echo "Email__Smtp__UseTls=true"
        # Production-valid public URLs (the API refuses http/localhost outside Development); the
        # simulation never calls them — the deploy's public check uses the local web port.
        echo "Cors__AllowedOrigins__0=https://tms.sim.example.test"
        echo "Frontend__BaseUrl=https://tms.sim.example.test"
        echo "Network__KnownProxies__0=127.0.0.1"
        echo "VITE_API_BASE_URL=http://127.0.0.1:$SIM_WEB_PORT/api"
        echo "Storage__RootPath=$STORAGE"
    } > "$ENV_FILE.tmp"
    local k
    for k in $(printf '%s' "${SIM_ENV_OMIT:-}" | tr ' ' '\n'); do
        grep -v "^$k=" "$ENV_FILE.tmp" > "$ENV_FILE.tmp2"; mv "$ENV_FILE.tmp2" "$ENV_FILE.tmp"
    done
    if [[ -n "${SIM_ENV_EXTRA:-}" ]]; then printf '%s\n' "$SIM_ENV_EXTRA" >> "$ENV_FILE.tmp"; fi
    mv "$ENV_FILE.tmp" "$ENV_FILE"
    chmod 600 "$ENV_FILE"
}

storage_manifest() {
    ( cd "$STORAGE" 2>/dev/null && find . -type f ! -name '.deploy-write-probe-*' -print | LC_ALL=C sort | while IFS= read -r f; do
        printf '%s  %s\n' "$(shasum -a 256 "$f" | cut -c1-16)" "$f"; done )
}

reset_fixture() {
    SIM_SERVICE_MODE=ready SIM_ROOT="$SIM_ROOT" API_BIN_DIR="$API_BIN_DIR" "$BIN/systemctl" stop transportationservice-api >/dev/null 2>&1
    kill_locker
    rm -rf "$STATE"; mkdir -p "$STATE"
    touch "$STATE/unit-installed"
    "${PSQL_SU[@]}" -d postgres -c "DROP DATABASE IF EXISTS tms_sim;" >/dev/null
    "${PSQL_SU[@]}" -d postgres -c "CREATE DATABASE tms_sim TEMPLATE tms_base OWNER tms;" >/dev/null || die "create tms_sim"
    # Previous release (stub binaries; they are never executed by the stub service).
    chmod -R u+w "$API_BIN_DIR" 2>/dev/null
    rm -rf "$API_BIN_DIR" "$WEB_DIST_DIR" "$BACKUPS" "$RELEASES" "$MAINT_FLAG" "$SIM_ROOT/run"/*
    mkdir -p "$API_BIN_DIR" "$WEB_DIST_DIR" "$BACKUPS" "$RELEASES" "$SIM_ROOT/run"
    printf 'previous-release-binary\n' > "$API_BIN_DIR/TransportationService.Api.dll"
    printf '{\n  "version": "0.1.0",\n  "commit": "previous0"\n}\n' > "$API_BIN_DIR/deployment.json"
    printf '<!doctype html><title>previous web</title>\n' > "$WEB_DIST_DIR/index.html"
    PREV_BIN_SHA="$(shasum -a 256 "$API_BIN_DIR/TransportationService.Api.dll" | cut -c1-12)"
    # Durable storage (outside every release dir).
    chmod -R u+w "$STORAGE" 2>/dev/null
    rm -rf "$STORAGE"
    mkdir -p "$STORAGE/tenant-11111111/orders" "$STORAGE/tenant-11111111/pod" "$STORAGE/backups" "$STORAGE/message-sink"
    printf 'POD signature (sim)\n' > "$STORAGE/tenant-11111111/pod/signature.png"
    printf 'delivery note (sim)\n' > "$STORAGE/tenant-11111111/orders/cmr.pdf"
    printf 'in-app backup (sim)\n' > "$STORAGE/backups/inapp-20261019.dump"
    printf 'dev mail (sim)\n' > "$STORAGE/message-sink/mail.eml"
    STORAGE_BEFORE="$(storage_manifest)"
    write_env
    # The previous release is running.
    SIM_SERVICE_MODE=ready SIM_ROOT="$SIM_ROOT" SIM_API_PORT="$SIM_API_PORT" API_BIN_DIR="$API_BIN_DIR" \
        "$BIN/systemctl" start transportationservice-api
    local i
    for i in 1 2 3 4 5 6 7 8 9 10; do
        [[ "$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$SIM_API_PORT/health/ready")" == "200" ]] && break
        sleep 0.5
    done
    : > "$SIM_EVENTS"
    if [[ -n "${SIM_PRE_DEPLOY_SQL:-}" ]]; then "${PSQL_SU[@]}" -d tms_sim -f "$SIM_PRE_DEPLOY_SQL" >/dev/null || die "pre-deploy SQL"; fi
}

# run_deploy <scenario> <ref|-> [KEY=VALUE ...]  — runs the real deploy script, sets RC.
run_deploy() {
    local scn="$1" ref="$2"; shift 2
    local log="$LOGS/$scn.log"
    (
        export DEPLOY_SIMULATE=1 REPO_DIR="$REPO" DEPLOY_USER="$(id -un)" DEPLOY_HOME="$HOME" \
            SERVICE_NAME=transportationservice-api ENV_FILE="$ENV_FILE" BACKUP_DIR="$BACKUPS" \
            STAGE_ROOT="$RELEASES" MAINTENANCE_FLAG="$MAINT_FLAG" API_BIN_DIR="$API_BIN_DIR" WEB_DIST_DIR="$WEB_DIST_DIR" \
            LOCAL_API_URL="http://127.0.0.1:$SIM_API_PORT" PUBLIC_URL="http://127.0.0.1:$SIM_WEB_PORT" \
            STARTUP_WAIT_SECONDS=20 LOCK_FILE="$SIM_ROOT/run/deploy.lock" PGPASS_DIR="$SIM_ROOT/run" \
            DOTNET="$BIN/dotnet" DOTNET_EF="$BIN/dotnet-ef" NPM="$BIN/npm" PSQL="$PGBIN/psql" \
            PG_DUMP="$BIN/pg_dump" PG_RESTORE="$PGBIN/pg_restore" SYSTEMCTL="$BIN/systemctl" \
            JOURNALCTL="$BIN/journalctl" NGINX="$BIN/nginx" DOTNET_ROOT="$REAL_DOTNET_ROOT" \
            BUILD_PATH="$REAL_DOTNET_ROOT:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin" \
            SIM_ROOT="$SIM_ROOT" SIM_EVENTS="$SIM_EVENTS" SIM_API_PORT="$SIM_API_PORT" \
            SIM_SERVICE_MODE="${SIM_SERVICE_MODE:-ready}"
        local kv
        for kv in "$@"; do export "$kv"; done
        if [[ "$ref" == "-" ]]; then /bin/bash "$DEPLOY_SCRIPT"; else /bin/bash "$DEPLOY_SCRIPT" "$ref"; fi
    ) > "$log" 2>&1
    RC=$?
    LOG="$log"
}

# ----------------------------------------------------------- assertions ----
SCN_OK=1
ok() { printf '    PASS  %s\n' "$1"; TOTAL_PASS=$((TOTAL_PASS + 1)); }
nok() { printf '    FAIL  %s\n' "$1"; TOTAL_FAIL=$((TOTAL_FAIL + 1)); SCN_OK=0; }
check() { local d="$1"; shift; if "$@"; then ok "$d"; else nok "$d"; fi; }
rc_is() { [[ "$RC" == "$1" ]]; }
log_has() { grep -qE -- "$1" "$LOG"; }
ev_has() { grep -qE -- "$1" "$SIM_EVENTS"; }
ev_line() { grep -nE -- "$1" "$SIM_EVENTS" | head -n 1 | cut -d: -f1; }
ev_before() { local a b; a="$(ev_line "$1")"; b="$(ev_line "$2")"; [[ -n "$a" && -n "$b" && "$a" -lt "$b" ]]; }
# events after the deploy's first 'systemctl stop'
ev_after_stop() { local s; s="$(ev_line $'systemctl\tstop')"; [[ -n "$s" ]] && tail -n +"$s" "$SIM_EVENTS"; }
starts_after_stop() { ev_after_stop | grep -cE $'systemctl\tstart' || true; }
no_previous_start() { ! ev_after_stop | grep -qE $'systemctl\tstart .*sha='"$PREV_BIN_SHA"; }
svc_active() { SIM_ROOT="$SIM_ROOT" API_BIN_DIR="$API_BIN_DIR" "$BIN/systemctl" is-active --quiet transportationservice-api; }
svc_inactive() { ! svc_active; }
storage_unchanged() { [[ "$(storage_manifest)" == "$STORAGE_BEFORE" ]]; }
bin_commit_is() { grep -q "\"commit\": \"$1\"" "$API_BIN_DIR/deployment.json"; }
db_count() { "${PSQL_SU[@]}" -d tms_sim -c 'SELECT count(*) FROM "__EFMigrationsHistory";'; }
db_count_is() { [[ "$(db_count)" == "$1" ]]; }
maint_on() { [[ -f "$MAINT_FLAG" ]]; }
maint_off() { [[ ! -f "$MAINT_FLAG" ]]; }
nothing_touched() { ! ev_has $'systemctl\tstop' && ! ev_has $'pg_dump' && ! ev_has $'dotnet-ef\tdatabase'; }
no_build() { ! ev_has $'dotnet\tbuild'; }
no_secret_keys() { [[ -f "$1" ]] && ! grep -qE '^(ConnectionStrings__|Jwt__|ColumnEncryption__|Email__Smtp__Password|Storage__)' "$1"; }
ef_env_minimal() { local f="$STATE/env-dotnet-ef-database-update.keys"; [[ -f "$f" ]] && grep -q '^ConnectionStrings__DefaultConnection$' "$f" && ! grep -qE '^(Jwt__|ColumnEncryption__|Email__)' "$f"; }
newest_stage() { ls -1dt "$RELEASES"/stage-* 2>/dev/null | head -n 1; }
newest_file() { ls -1t $1 2>/dev/null | head -n 1; }

begin() {
    SCN="$1"; SCN_OK=1
    printf '\n=== scenario: %s%s\n' "$1" "${2:+  ($2)}"
}
finish() {
    local status=PASS; [[ $SCN_OK -eq 1 ]] || status=FAIL
    printf '    -> %s (exit %s, log %s)\n' "$status" "$RC" "$LOG"
    SUMMARY="$SUMMARY$(printf '%-28s %-5s exit=%s' "$SCN" "$status" "$RC")"$'\n'
}

refused_before_change() {
    check "nothing touched (no stop, no dump, no migration)" nothing_touched
    check "no build ran" no_build
    check "previous release still active" svc_active
    check "previous binaries untouched" bin_commit_is previous0
    check "database untouched ($BASE_COUNT migrations)" db_count_is "$BASE_COUNT"
    check "storage untouched" storage_unchanged
    check "maintenance page off" maint_off
}

forward_only_outcome() {
    check "exit code 1" rc_is 1
    check "message: FORWARD-ONLY" log_has 'FORWARD-ONLY'
    check "message: backup path printed" log_has "Databaseback-up: $BACKUPS/tms_sim-"
    check "message: restore-based runbook printed" log_has 'TERUGROLLEN VIA RESTORE'
    check "service stays stopped" svc_inactive
    check "previous binaries NOT started after the stop" no_previous_start
    check "maintenance page stays on" maint_on
    check "unit disabled so a reboot cannot start old binaries" ev_has $'systemctl\tdisable'
    check "storage untouched" storage_unchanged
    check "final dump taken with service stopped" ev_has $'pg_dump\tservice=inactive'
}

# ------------------------------------------------------------ scenarios ----
scn_no-ref() {
    begin no-ref "git ref is mandatory"
    reset_fixture; run_deploy no-ref -
    check "exit code 2 (refused)" rc_is 2
    check "message: geen git-ref" log_has 'geen git-ref opgegeven'
    refused_before_change
    finish
}

storage_refusal() {  # <scenario> <pattern> [prep]
    check "exit code 2 (refused)" rc_is 2
    check "message: $2" log_has "$2"
    refused_before_change
}

scn_storage-unset() {
    begin storage-unset "Storage__RootPath missing"
    SIM_ENV_OMIT="Storage__RootPath" reset_fixture
    run_deploy storage-unset "$SIM_REF"
    storage_refusal storage-unset 'Storage__RootPath ontbreekt'
    finish
}
scn_storage-relative() {
    begin storage-relative "relative Storage__RootPath"
    SIM_ENV_OMIT="Storage__RootPath" SIM_ENV_EXTRA="Storage__RootPath=App_Data" reset_fixture
    run_deploy storage-relative "$SIM_REF"
    storage_refusal storage-relative 'is niet absoluut'
    finish
}
scn_storage-missing() {
    begin storage-missing "Storage__RootPath does not exist"
    SIM_ENV_OMIT="Storage__RootPath" SIM_ENV_EXTRA="Storage__RootPath=$SRV/does-not-exist" reset_fixture
    run_deploy storage-missing "$SIM_REF"
    storage_refusal storage-missing 'bestaat niet'
    check "script did not create the storage root" test ! -e "$SRV/does-not-exist"
    finish
}
scn_storage-unwritable() {
    begin storage-unwritable "Storage__RootPath not writable by the service user"
    reset_fixture
    chmod 0555 "$STORAGE"
    run_deploy storage-unwritable "$SIM_REF"
    chmod 0755 "$STORAGE"
    storage_refusal storage-unwritable 'niet schrijfbaar'
    finish
}
scn_storage-inside-release() {
    begin storage-inside-release "Storage__RootPath inside the API release dir"
    SIM_ENV_OMIT="Storage__RootPath" SIM_ENV_EXTRA="Storage__RootPath=$API_BIN_DIR/storage" reset_fixture
    mkdir -p "$API_BIN_DIR/storage"
    run_deploy storage-inside-release "$SIM_REF"
    storage_refusal storage-inside-release 'ligt BINNEN'
    finish
}
scn_legacy-app-data() {
    begin legacy-app-data "legacy App_Data with files inside the bin dir"
    reset_fixture
    mkdir -p "$API_BIN_DIR/App_Data/tenant-11111111"
    printf 'legacy upload\n' > "$API_BIN_DIR/App_Data/tenant-11111111/legacy.pdf"
    run_deploy legacy-app-data "$SIM_REF"
    storage_refusal legacy-app-data 'legacy App_Data in de release-map'
    check "message points to docs/storage.md" log_has 'docs/storage.md'
    check "legacy file not moved/deleted" test -f "$API_BIN_DIR/App_Data/tenant-11111111/legacy.pdf"
    finish
}
scn_restore-previous-pending() {
    begin restore-previous-pending "RESTORE_PREVIOUS_ON_FAILURE=1 with pending migrations"
    reset_fixture
    run_deploy restore-previous-pending "$SIM_REF" RESTORE_PREVIOUS_ON_FAILURE=1
    check "exit code 2 (refused)" rc_is 2
    check "message: FORWARD-ONLY refusal" log_has 'RESTORE_PREVIOUS_ON_FAILURE=1 met openstaande migraties'
    refused_before_change
    finish
}

scn_success() {
    begin success "normal release, 3 pending migrations"
    reset_fixture
    mkdir -p "$API_BIN_DIR/App_Data/empty-legacy-dir"   # no files: allowed, must survive rsync --delete
    run_deploy success "$SIM_REF" MIGRATION_VERBOSE=1
    local stage snap dump
    stage="$(newest_stage)"; snap="$(newest_file "$BACKUPS/storage-tms_sim-*.tar.gz")"; dump="$(newest_file "$BACKUPS/tms_sim-*.dump")"
    check "exit code 0" rc_is 0
    check "summary printed" log_has 'DEPLOYMENT GESLAAGD'
    check "order: build before stop" ev_before $'dotnet\tbuild' $'systemctl\tstop'
    check "order: stop before final pg_dump" ev_before $'systemctl\tstop' $'pg_dump'
    check "final dump taken with service stopped" ev_has $'pg_dump\tservice=inactive'
    check "order: pg_dump before migration" ev_before $'pg_dump' $'dotnet-ef\tdatabase update'
    check "order: migration before start" ev_before $'dotnet-ef\tdatabase update' $'systemctl\tstart'
    check "order: start before nginx reload" ev_before $'systemctl\tstart' $'systemctl\treload nginx'
    check "migration conn: Command Timeout=0" ev_has $'dotnet-ef\tdatabase update.*ct=\\[Command Timeout=0\\]'
    check "migration conn: lock_timeout=60s" ev_has $'dotnet-ef\tdatabase update.*lt=\\[lock_timeout=60s\\]'
    check "migration loads the STAGED build" ev_has $'dotnet-ef\tdatabase update base='"$RELEASES"
    check "EF commands ran with CommandTimeout='0'" grep -q "CommandTimeout='0'" "$stage/migration.log"
    check "per-migration timings recorded (3)" test "$(grep -c . "$stage/migration-timings.tsv" 2>/dev/null)" = 3
    check "H10: dotnet build env has no tms.env keys" no_secret_keys "$STATE/env-dotnet-build.keys"
    check "H10: dotnet restore env has no tms.env keys" no_secret_keys "$STATE/env-dotnet-restore.keys"
    check "H10: npm ci env has no tms.env keys" no_secret_keys "$STATE/env-npm-ci.keys"
    check "H10: npm build env has no tms.env keys" no_secret_keys "$STATE/env-npm-run.keys"
    check "H10: npm build got VITE_API_BASE_URL" grep -q '^VITE_API_BASE_URL$' "$STATE/env-npm-run.keys"
    check "H10: dotnet-ef env = connection string only (no JWT/encryption/SMTP)" ef_env_minimal
    check "all $SOURCE_COUNT migrations applied" db_count_is "$SOURCE_COUNT"
    check "new binaries installed (commit $SHORT_REF)" bin_commit_is "$SHORT_REF"
    check "service active" svc_active
    check "one start after the stop, with the NEW binaries" test "$(starts_after_stop)" = 1
    check "previous binaries NOT started" no_previous_start
    check "maintenance page off" maint_off
    check "storage untouched" storage_unchanged
    check "empty legacy App_Data dir survived rsync --delete (exclude)" test -d "$API_BIN_DIR/App_Data/empty-legacy-dir"
    check "dump verified by pg_restore --list" sh -c "'$PGBIN/pg_restore' --list '$dump' | grep -q 'TABLE DATA public __EFMigrationsHistory'"
    check "storage snapshot exists" test -s "$snap"
    check "checksums verify" sh -c "cd '$BACKUPS' && shasum -a 256 -c \"\$(ls -1t tms_sim-*.sha256 | head -n 1)\" >/dev/null"
    check "snapshot contains tenant files" sh -c "tar -tzf '$snap' | grep -q 'tenant-11111111/pod/signature.png'"
    check "snapshot excludes backups/ and message-sink/" sh -c "tar -tzf '$snap' > '$STATE/snapshot.list' && ! grep -qE '(^|/)(backups|message-sink)(/|$)' '$STATE/snapshot.list'"
    check "previous binaries kept (without App_Data)" sh -c "test -f '$stage/previous-api/deployment.json' && test ! -e '$stage/previous-api/App_Data'"
    check "previous frontend kept" grep -q 'previous web' "$stage/previous-web/index.html"
    check "new frontend live" grep -q "commit=$SHORT_REF" "$WEB_DIST_DIR/index.html"
    check "no secret printed in the log" sh -c "! grep -qE '$DB_PASSWORD|$JWT_KEY|$SMTP_PASSWORD' '$LOG'"
    finish
}

scn_startup-never-ready() {
    begin startup-never-ready "app never becomes ready after the migration"
    reset_fixture
    SIM_SERVICE_MODE=never-ready run_deploy startup-never-ready "$SIM_REF" STARTUP_WAIT_SECONDS=10
    forward_only_outcome
    check "message: not ready" log_has 'werd niet ready'
    check "migrations were applied ($SOURCE_COUNT)" db_count_is "$SOURCE_COUNT"
    check "exactly one start after the stop (the new binaries)" test "$(starts_after_stop)" = 1
    check "new binaries in place, previous kept in stage" sh -c "grep -q '\"commit\": \"$SHORT_REF\"' '$API_BIN_DIR/deployment.json' && test -d '$(newest_stage)/previous-api'"
    check "frontend NOT switched" grep -q 'previous web' "$WEB_DIST_DIR/index.html"
    finish
}

scn_startup-crash() {
    begin startup-crash "app exits during startup (unit 'failed')"
    reset_fixture
    SIM_SERVICE_MODE=crash run_deploy startup-crash "$SIM_REF" STARTUP_WAIT_SECONDS=30
    forward_only_outcome
    check "message: failed" log_has "in 'failed' na de start"
    check "exactly one start after the stop (the new binaries)" test "$(starts_after_stop)" = 1
    finish
}

scn_lock-timeout() {
    begin lock-timeout "another session holds a lock during the migration (lock_timeout=3s)"
    reset_fixture
    local hook
    hook="nohup env PGAPPNAME=deploy-sim-locker '$PGBIN/psql' -h 127.0.0.1 -p $SIM_PG_PORT -U postgres -d tms_sim -X -q \
-c 'BEGIN; LOCK TABLE \"__EFMigrationsHistory\" IN ACCESS EXCLUSIVE MODE; SELECT pg_sleep(120); COMMIT;' >/dev/null 2>&1 & echo \$! > '$STATE/locker.pid';
for i in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do
  n=\$('$PGBIN/psql' -h 127.0.0.1 -p $SIM_PG_PORT -U postgres -d tms_sim -X -A -t -c \"SELECT count(*) FROM pg_locks l JOIN pg_class c ON c.oid=l.relation WHERE c.relname='__EFMigrationsHistory' AND l.mode='AccessExclusiveLock' AND l.granted\");
  [ \"\$n\" = 1 ] && break; sleep 0.25; done"
    local t0=$SECONDS
    run_deploy lock-timeout "$SIM_REF" MIGRATION_LOCK_TIMEOUT=3s SIM_AFTER_PG_DUMP_HOOK="$hook"
    local dt=$((SECONDS - t0))
    kill_locker
    forward_only_outcome
    check "lock timeout reported (55P03)" log_has '55P03|lock timeout'
    check "message: Oorzaak lock_timeout" log_has 'Oorzaak: lock_timeout'
    check "message: applied-count reported, or history unreadable while locked" \
        log_has 'In deze run toegepast: 0 van 3|Migratiehistoriek nu niet leesbaar'    check "database unchanged ($BASE_COUNT)" db_count_is "$BASE_COUNT"
    check "no start at all after the stop" test "$(starts_after_stop)" = 0
    check "binaries untouched (previous still in place, not started)" bin_commit_is previous0
    check "failed fast incl. error handler (< 60 s; lock held 120 s), took ${dt}s" test "$dt" -lt 60
    finish
}

scn_migration-failure() {
    begin migration-failure "4th pending migration (test-only) raises after 3 real ones committed"
    reset_fixture
    run_deploy migration-failure sim/forced-failure
    forward_only_outcome
    check "EF error shown" log_has 'deploy-sim: forced migration failure'
    check "message: 3 of 4 applied in this run" log_has 'In deze run toegepast: 3 van 4'
    check "partial schema: $SOURCE_COUNT applied" db_count_is "$SOURCE_COUNT"
    check "no start at all after the stop" test "$(starts_after_stop)" = 0
    check "binaries untouched (previous still in place, not started)" bin_commit_is previous0
    check "timings mark the failed migration" grep -q 'MISLUKT' "$(newest_stage)/migration-timings.tsv"
    finish
}

scn_db-ahead-of-code() {
    begin db-ahead-of-code "after a migrated release, re-deploy the OLD ref with RESTORE_PREVIOUS_ON_FAILURE=1"
    reset_fixture
    ef_harness tms_sim database update > "$LOGS/db-ahead-of-code.prep.log" 2>&1
    run_deploy db-ahead-of-code "$OLD_REF" RESTORE_PREVIOUS_ON_FAILURE=1
    check "exit code 2 (refused)" rc_is 2
    check "message: schema newer than code" log_has 'code-only rollback'
    check "nothing touched (no stop, no dump, no migration)" nothing_touched
    check "no build ran" no_build
    check "previous release still active" svc_active
    check "database still at $SOURCE_COUNT" db_count_is "$SOURCE_COUNT"
    check "storage untouched" storage_unchanged
    finish
}

# App-level scenarios: start the REAL built API (ASPNETCORE_ENVIRONMENT=Production) and wait for
# GET /health/ready. They pass only once the integrated app has /health/ready (and accepts
# Storage__RootPath); before integration they document the current behaviour.
scn_app-real-success() {
    begin app-real-success "APP-LEVEL (needs integration): real API, Production, /health/ready"
    reset_fixture
    SIM_SERVICE_MODE=real run_deploy app-real-success "$SIM_REF" STARTUP_WAIT_SECONDS=180
    check "exit code 0" rc_is 0
    check "service active" svc_active
    check "new binaries installed" bin_commit_is "$SHORT_REF"
    check "GET /health/ready = 200 after the deploy" \
        test "$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$SIM_API_PORT/health/ready")" = 200
    check "maintenance page off" maint_off
    check "final dump taken with service stopped" ev_has $'pg_dump\tservice=inactive'
    # SMTP is unreachable in every simulated deploy (nothing listens on 127.0.0.1:2525): mail is not a
    # readiness dependency, the release goes live and the outbox retries.
    check "ready although SMTP is unreachable" svc_active
    finish
}
scn_app-bad-frontend-url() {
    begin app-bad-frontend-url "APP-LEVEL: Frontend__BaseUrl http/localhost -> app refuses to start, forward-only"
    SIM_ENV_OMIT="Frontend__BaseUrl" SIM_ENV_EXTRA="Frontend__BaseUrl=http://127.0.0.1:$SIM_WEB_PORT" reset_fixture
    SIM_SERVICE_MODE=real run_deploy app-bad-frontend-url "$SIM_REF" STARTUP_WAIT_SECONDS=60
    forward_only_outcome
    check "service log names Frontend:BaseUrl" grep -q 'Frontend:BaseUrl' "$STATE/service.log"
    finish
}
scn_app-peppol-live() {
    begin app-peppol-live "APP-LEVEL: a Peppol settings row on Live without a real provider -> app runs, Peppol off, critical log"
    local sql="$SIM_ROOT/peppol-live.sql"
    cat > "$sql" <<'SQL'
-- The schema-only fixture has no tenant: one sim tenant + legal entity carry the Live row.
INSERT INTO tenants ("Id","Name","Slug","IsActive","CreatedAt")
VALUES ('5150a000-0000-4000-8000-000000000001','Sim Peppol','sim-peppol',true,now()) ON CONFLICT DO NOTHING;
INSERT INTO legal_entities ("Id","LegalName","DefaultCurrency","PaymentTermDays","InvoiceNumberFormat","InvoiceSequencePadding","IsActive","IsDefault","TenantId","CreatedAt","UpdatedAt","IsDeleted")
VALUES ('5150a000-0000-4000-8000-000000000002','Sim Peppol BV','EUR',30,'FAC-{year}{seq}',6,true,true,'5150a000-0000-4000-8000-000000000001',now(),now(),false)
ON CONFLICT DO NOTHING;
INSERT INTO peppol_settings ("Id","LegalEntityId","Enabled","Environment","ProviderKey","TenantId","CreatedAt","UpdatedAt","IsDeleted")
SELECT gen_random_uuid(), le."Id", true, 'Live', 'sandbox', le."TenantId", now(), now(), false
FROM legal_entities le
WHERE NOT EXISTS (SELECT 1 FROM peppol_settings ps WHERE ps."LegalEntityId" = le."Id")
LIMIT 1;
UPDATE peppol_settings SET "Environment" = 'Live', "Enabled" = true;
SQL
    SIM_PRE_DEPLOY_SQL="$sql" reset_fixture
    SIM_SERVICE_MODE=real run_deploy app-peppol-live "$SIM_REF" STARTUP_WAIT_SECONDS=180
    check "exit code 0 (Peppol is optional)" rc_is 0
    check "service active" svc_active
    check "critical log: Peppol LIVE without a live-capable provider" grep -q 'no live-capable Peppol provider is registered' "$STATE/service.log"
    finish
}
scn_app-missing-encryption-key() {
    begin app-missing-encryption-key "APP-LEVEL: ColumnEncryption__Key missing -> app refuses to start"
    SIM_ENV_OMIT="ColumnEncryption__Key" reset_fixture
    SIM_SERVICE_MODE=real run_deploy app-missing-encryption-key "$SIM_REF" STARTUP_WAIT_SECONDS=60
    forward_only_outcome
    check "service log mentions ColumnEncryption" grep -q 'ColumnEncryption' "$STATE/service.log"
    finish
}
scn_app-db-down-at-start() {
    begin app-db-down-at-start "APP-LEVEL: database stops between migration and app start"
    reset_fixture
    SIM_SERVICE_MODE=real SIM_BEFORE_START_HOOK="'$PGBIN/pg_ctl' -D '$PGDATA' -m fast -w stop" \
        run_deploy app-db-down-at-start "$SIM_REF" STARTUP_WAIT_SECONDS=60
    "$PGBIN/pg_ctl" -D "$PGDATA" -l "$LOGS/postgres.log" -w start >/dev/null
    check "exit code 1" rc_is 1
    check "message: FORWARD-ONLY" log_has 'FORWARD-ONLY'
    check "service stays stopped" svc_inactive
    check "previous binaries NOT started after the stop" no_previous_start
    check "storage untouched" storage_unchanged
    finish
}

# ------------------------------------------------------------------ main ----
say "SIM_ROOT=$SIM_ROOT"
check_prereqs
setup_dirs
setup_pg
setup_repo
setup_base_db
start_web
trap 'teardown' EXIT
bash -n "$DEPLOY_SCRIPT" || die "deploy script has syntax errors"

for s in $(printf '%s\n' $EXPANDED | tr ' ' '\n' | grep .); do
    if declare -F "scn_$s" >/dev/null; then "scn_$s"; else say "unknown scenario: $s"; TOTAL_FAIL=$((TOTAL_FAIL + 1)); fi
done

printf '\n================ deploy-sim summary ================\n%s' "$SUMMARY" | tee "$LOGS/summary.txt"
printf 'assertions: %s passed, %s failed\nlogs: %s\n' "$TOTAL_PASS" "$TOTAL_FAIL" "$LOGS" | tee -a "$LOGS/summary.txt"
[[ "$TOTAL_FAIL" -eq 0 ]]
