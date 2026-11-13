#!/usr/bin/env bash
#
# DyeHouseERP full backup wrapper.
#
# Validates the database name against an explicit allow-list (the only
# protection against backing up - or, worse, scripting against - the wrong
# database), then runs backup-dyehouse.sql through sqlcmd.
#
#   ./backup-dyehouse.sh --database DyeHouseERP --backup-dir /var/backups/dyehouse \
#       --server db01 --user sa --password-env DYEHOUSE_BACKUP_PASSWORD
#
# Options:
#   --server NAME        SQL Server host (default: localhost)
#   --database NAME      database to back up (required; must match
#                        ^[A-Za-z0-9_]+$ and be on the allow-list)
#   --backup-dir DIR     server-side backup directory (required)
#   --user LOGIN         SQL login (omit for trusted/integrated auth)
#   --password-env VAR   env var holding the SQL login password
#                        (default: DYEHOUSE_BACKUP_PASSWORD; required with --user)
#   --allow-db NAME      extra allowed database (repeatable). The built-in
#                        default allow-list is "DyeHouseERP". Anything else must
#                        be listed here or via DYEHOUSE_BACKUP_ALLOW_DBS
#                        (comma-separated).
#   --backup-name NAME   fixed file name instead of the timestamped default
#   --path-separator C   "\" (Windows host) or "/" (Linux host), default "\"
#
#   --trust-server-cert  pass sqlcmd -C (trust a server certificate that is not
#                        in the host's trust store). The sqlcmd 18 driver
#                        encrypts and validates by default, so CI/test
#                        containers with a self-signed certificate need this.
#
# Exit codes: 0 backup written, 1 usage/validation error, 2 sqlcmd failure,
# 3 sqlcmd not installed.
set -euo pipefail

SERVER="localhost"
DATABASE=""
BACKUP_DIR=""
USER_LOGIN=""
PASSWORD_ENV="DYEHOUSE_BACKUP_PASSWORD"
PATH_SEP='\'
BACKUP_NAME=""
TRUST_CERT=0
ALLOW_EXTRA=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        --server) SERVER="$2"; shift 2 ;;
        --database) DATABASE="$2"; shift 2 ;;
        --backup-dir) BACKUP_DIR="$2"; shift 2 ;;
        --user) USER_LOGIN="$2"; shift 2 ;;
        --password-env) PASSWORD_ENV="$2"; shift 2 ;;
        --allow-db) ALLOW_EXTRA+=("$2"); shift 2 ;;
        --path-separator) PATH_SEP="$2"; shift 2 ;;
        --trust-server-cert) TRUST_CERT=1; shift ;;
        --backup-name) BACKUP_NAME="$2"; shift 2 ;;
        -h|--help) sed -n '2,/^set -euo/p' "$0"; exit 0 ;;
        *) echo "backup-dyehouse.sh: unknown option $1" >&2; exit 1 ;;
    esac
done

[[ -n "$DATABASE" ]] || { echo "backup-dyehouse.sh: --database is required" >&2; exit 1; }
[[ -n "$BACKUP_DIR" ]] || { echo "backup-dyehouse.sh: --backup-dir is required" >&2; exit 1; }

if [[ -n "$BACKUP_NAME" ]]; then
    if [[ "$BACKUP_NAME" == */* || "$BACKUP_NAME" != *.bak ]]; then
        echo "backup-dyehouse.sh: refusing --backup-name '$BACKUP_NAME' (file name only, must end in .bak)" >&2
        exit 1
    fi
    _bs=$'\\'
    if [[ "$BACKUP_NAME" == *"$_bs"* ]]; then
        echo "backup-dyehouse.sh: refusing --backup-name '$BACKUP_NAME' (file name only, must end in .bak)" >&2
        exit 1
    fi
fi

if [[ ! "$DATABASE" =~ ^[A-Za-z0-9_]+$ ]]; then
    echo "backup-dyehouse.sh: refusing database name '$DATABASE' (allowed: letters, digits, underscore)" >&2
    exit 1
fi

# Explicit allow-list: the built-in production name plus anything the operator
# adds. Nothing else is ever backed up by this script.
ALLOWED=("DyeHouseERP")
if [[ -n "${DYEHOUSE_BACKUP_ALLOW_DBS:-}" ]]; then
    IFS=',' read -r -a ENV_ALLOW <<< "$DYEHOUSE_BACKUP_ALLOW_DBS"
    ALLOWED+=("${ENV_ALLOW[@]}")
fi
ALLOWED+=("${ALLOW_EXTRA[@]:-}")

if ! printf '%s\n' "${ALLOWED[@]}" | grep -qx "$DATABASE"; then
    echo "backup-dyehouse.sh: refusing '$DATABASE': not on the allow-list (${ALLOWED[*]})" >&2
    echo "  add --allow-db '$DATABASE' if you really mean it." >&2
    exit 1
fi

command -v sqlcmd >/dev/null 2>&1 || { echo "backup-dyehouse.sh: sqlcmd not found on PATH" >&2; exit 3; }

AUTH_ARGS=()
if [[ -n "$USER_LOGIN" ]]; then
    PASSWORD="${!PASSWORD_ENV:-}"
    [[ -n "$PASSWORD" ]] || { echo "backup-dyehouse.sh: --user given but \$$PASSWORD_ENV is empty" >&2; exit 1; }
    AUTH_ARGS=(-U "$USER_LOGIN" -P "$PASSWORD")
fi

TLS_ARGS=()
if [[ "$TRUST_CERT" -eq 1 ]]; then TLS_ARGS=(-C); fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# -b aborts on error, -V16 makes RAISERROR(>=11) fail the process: a failed
# backup is never reported as success.
if ! sqlcmd -S "$SERVER" -d master -b -V16 "${TLS_ARGS[@]}" "${AUTH_ARGS[@]}" \
    -v DbName="$DATABASE" BackupDir="$BACKUP_DIR" PathSeparator="$PATH_SEP" BackupName="$BACKUP_NAME" \
    -i "$SCRIPT_DIR/backup-dyehouse.sql"; then
    echo "backup-dyehouse.sh: BACKUP DATABASE failed for '$DATABASE'" >&2
    exit 2
fi

echo "backup-dyehouse.sh: backup of '$DATABASE' completed."
