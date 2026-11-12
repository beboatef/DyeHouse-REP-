#!/usr/bin/env bash
#
# DyeHouseERP restore drill wrapper.
#
# Restores a backup into a SCRATCH database and verifies it (see
# restore-verify.sql): VERIFYONLY, CHECKDB, ledger-sum reconciliation against
# the source database, key-table row counts, then drops the scratch database.
# Every step's output goes to the report file; any failure exits non-zero and
# - by design of the .sql - a failed drill never reaches the drop step.
#
#   ./restore-verify.sh --source-db DyeHouseERP \
#       --backup-file /mnt/backups/DyeHouseERP_20260101_000000.bak \
#       --scratch-db DyeHouseERP_restoretest \
#       --report /var/log/dyehouse/restore-drill.txt \
#       --server db01 --user sa --password-env DYEHOUSE_BACKUP_PASSWORD \
#       --allow-db DyeHouseERP
#
# Options:
#   --source-db NAME     live database the backup was taken from (required;
#                        must match ^[A-Za-z0-9_]+$ and be on the allow-list)
#   --backup-file PATH   server-side path of the backup file (required)
#   --scratch-db NAME    restore target (required; MUST end in _restoretest
#                        and must differ from --source-db)
#   --report PATH        report file the drill output is written to (required)
#   --server NAME        SQL Server host (default: localhost)
#   --user LOGIN         SQL login (omit for trusted/integrated auth)
#   --password-env VAR   env var holding the SQL login password
#                        (default: DYEHOUSE_BACKUP_PASSWORD; required with --user)
#   --allow-db NAME      allowed source database (repeatable, or comma-separated
#                        via DYEHOUSE_RESTORE_ALLOW_DBS). There is NO built-in
#                        default: the operator names the source explicitly.
#   --data-dir DIR       server-side directory for relocated scratch files
#                        (passed through to the .sql; empty = server default)
#   --path-separator C   "\" (Windows host) or "/" (Linux host), default "\"
#
#   --keep-scratch       leave the scratch database in place after a successful
#                        drill (for follow-up application-level checks)
#
# Exit codes: 0 drill passed, 1 usage/validation error, 2 drill failed
# (details in the report), 3 sqlcmd not installed.
set -euo pipefail

SERVER="localhost"
SOURCE_DB=""
BACKUP_FILE=""
SCRATCH_DB=""
REPORT=""
USER_LOGIN=""
PASSWORD_ENV="DYEHOUSE_BACKUP_PASSWORD"
DATA_DIR=""
PATH_SEP='\'
KEEP_SCRATCH=0
ALLOW=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        --source-db) SOURCE_DB="$2"; shift 2 ;;
        --backup-file) BACKUP_FILE="$2"; shift 2 ;;
        --scratch-db) SCRATCH_DB="$2"; shift 2 ;;
        --report) REPORT="$2"; shift 2 ;;
        --server) SERVER="$2"; shift 2 ;;
        --user) USER_LOGIN="$2"; shift 2 ;;
        --password-env) PASSWORD_ENV="$2"; shift 2 ;;
        --allow-db) ALLOW+=("$2"); shift 2 ;;
        --data-dir) DATA_DIR="$2"; shift 2 ;;
        --path-separator) PATH_SEP="$2"; shift 2 ;;
        --keep-scratch) KEEP_SCRATCH=1; shift ;;
        -h|--help) sed -n '2,/^set -euo/p' "$0"; exit 0 ;;
        *) echo "restore-verify.sh: unknown option $1" >&2; exit 1 ;;
    esac
done

[[ -n "$SOURCE_DB" ]] || { echo "restore-verify.sh: --source-db is required" >&2; exit 1; }
[[ -n "$BACKUP_FILE" ]] || { echo "restore-verify.sh: --backup-file is required" >&2; exit 1; }
[[ -n "$SCRATCH_DB" ]] || { echo "restore-verify.sh: --scratch-db is required" >&2; exit 1; }
[[ -n "$REPORT" ]] || { echo "restore-verify.sh: --report is required" >&2; exit 1; }

if [[ ! "$SOURCE_DB" =~ ^[A-Za-z0-9_]+$ ]] || [[ ! "$SCRATCH_DB" =~ ^[A-Za-z0-9_]+$ ]]; then
    echo "restore-verify.sh: refusing database name (allowed: letters, digits, underscore)" >&2
    exit 1
fi

# Guard 1 (belt): the scratch name must end in _restoretest - the .sql repeats
# this check (suspenders) so no wrapper bug can ever drop a live database.
if [[ "$SCRATCH_DB" != *_restoretest ]]; then
    echo "restore-verify.sh: refusing scratch name '$SCRATCH_DB': must end in _restoretest" >&2
    exit 1
fi

if [[ "$SCRATCH_DB" == "$SOURCE_DB" ]]; then
    echo "restore-verify.sh: refusing: scratch and source are the same database" >&2
    exit 1
fi

# Guard 2: the source must be explicitly allowed. There is deliberately no
# built-in default - restoring FROM an arbitrary database is the mistake this
# stops, so the operator names it every time (or exports the env var).
if [[ -n "${DYEHOUSE_RESTORE_ALLOW_DBS:-}" ]]; then
    IFS=',' read -r -a ENV_ALLOW <<< "$DYEHOUSE_RESTORE_ALLOW_DBS"
    ALLOW+=("${ENV_ALLOW[@]}")
fi
if ! printf '%s\n' "${ALLOW[@]:-}" | grep -qx "$SOURCE_DB"; then
    echo "restore-verify.sh: refusing source '$SOURCE_DB': not on the allow-list." >&2
    echo "  pass --allow-db '$SOURCE_DB' (or set DYEHOUSE_RESTORE_ALLOW_DBS) if you really mean it." >&2
    exit 1
fi

command -v sqlcmd >/dev/null 2>&1 || { echo "restore-verify.sh: sqlcmd not found on PATH" >&2; exit 3; }

AUTH_ARGS=()
if [[ -n "$USER_LOGIN" ]]; then
    PASSWORD="${!PASSWORD_ENV:-}"
    [[ -n "$PASSWORD" ]] || { echo "restore-verify.sh: --user given but \$$PASSWORD_ENV is empty" >&2; exit 1; }
    AUTH_ARGS=(-U "$USER_LOGIN" -P "$PASSWORD")
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPORT_DIR="$(dirname "$REPORT")"
mkdir -p "$REPORT_DIR"
TRANSCRIPT="$(mktemp)"

# -b aborts on error, -V16 makes RAISERROR(>=11) fail the process; -o writes
# the whole drill transcript to a temp file (-o overwrites, so the header is
# prepended afterwards to keep the report chronological).
if ! sqlcmd -S "$SERVER" -d master -b -V16 "${AUTH_ARGS[@]}" \
    -v SourceDb="$SOURCE_DB" BackupFile="$BACKUP_FILE" ScratchDb="$SCRATCH_DB" \
       DataDir="$DATA_DIR" PathSeparator="$PATH_SEP" KeepScratch="$KEEP_SCRATCH" \
    -i "$SCRIPT_DIR/restore-verify.sql" -o "$TRANSCRIPT"; then
    {
        echo "DyeHouseERP restore drill - $(date -u +%Y-%m-%dT%H:%M:%SZ) - FAILED"
        echo "source=$SOURCE_DB backup=$BACKUP_FILE scratch=$SCRATCH_DB server=$SERVER"
        echo "----------------------------------------------------------------"
        cat "$TRANSCRIPT"
    } > "$REPORT"
    rm -f "$TRANSCRIPT"
    echo "restore-verify.sh: DRILL FAILED - see $REPORT" >&2
    exit 2
fi

{
    echo "DyeHouseERP restore drill - $(date -u +%Y-%m-%dT%H:%M:%SZ) - PASSED"
    echo "source=$SOURCE_DB backup=$BACKUP_FILE scratch=$SCRATCH_DB server=$SERVER"
    echo "----------------------------------------------------------------"
    cat "$TRANSCRIPT"
} > "$REPORT"
rm -f "$TRANSCRIPT"

echo "restore-verify.sh: DRILL PASSED - report at $REPORT"
