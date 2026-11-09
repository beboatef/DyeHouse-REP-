#!/usr/bin/env sh
#
# ONE COMMAND for the warehouse screenshots on a REAL SQL Server:
#   API (dotnet run)  ->  real demo stock posted through the API  ->  Vite dev server
#
# Usage, from anywhere inside the repo:
#
#   ConnectionStrings__DefaultConnection='Server=localhost,1433;Database=DyeHouseERP;User Id=sa;Password=<pw>;TrustServerCertificate=True;' \
#     sh tools/run-warehouse-demo.sh
#
#   (Git Bash or WSL on Windows; bash/sh on macOS and Linux.)
#
# What it needs:
#   * a reachable SQL Server database the API may create/migrate (the startup applies the migrations
#     and seeds the four warehouses WH-RAW / WH-RDY / WH-MAT / WH-SUP plus the admin login);
#   * dotnet SDK 9, node 18+ (fetch), npm, curl.
#
# What it will NOT do:
#   * it never prints or writes the connection string, and it never writes a JWT key to disk;
#   * it posts nothing that is not a real document through the public API - no mock data, no direct
#     INSERTs, no stub server.
#
# If Jwt__Key is not set, a random key is generated for THIS run only (the API refuses to start
# without one). Every document created is real and labelled with the DEMO prefix.
#
set -eu

ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"

# Logs stay OUTSIDE the repository so a demo run never leaves untracked files in the working tree.
LOG_DIR="${TMPDIR:-/tmp}/dyehouse-warehouse-demo"
mkdir -p "$LOG_DIR"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

command -v dotnet >/dev/null 2>&1 || fail "dotnet SDK 9 is required (dotnet --version)."
command -v node   >/dev/null 2>&1 || fail "node 18+ is required (node --version)."
command -v npm    >/dev/null 2>&1 || fail "npm is required."
command -v curl   >/dev/null 2>&1 || fail "curl is required."

# ---- 1. the database this run works on ------------------------------------------------------------
if [ -z "${ConnectionStrings__DefaultConnection:-}" ]; then
  if [ -n "${DYEHOUSE_DEMO_CONNECTION:-}" ]; then
    ConnectionStrings__DefaultConnection="$DYEHOUSE_DEMO_CONNECTION"
    export ConnectionStrings__DefaultConnection
  else
    echo "ConnectionStrings__DefaultConnection is not set." >&2
    echo "" >&2
    echo "Point it at the SQL Server database this demo may use, for example:" >&2
    echo "  ConnectionStrings__DefaultConnection='Server=localhost,1433;Database=DyeHouseERP_Demo;User Id=sa;Password=<pw>;TrustServerCertificate=True;'" >&2
    echo "" >&2
    echo "The API applies the migrations and seeds the four warehouses + the admin login on startup." >&2
    exit 2
  fi
fi
echo "Database: configured (value not displayed)."

# ---- 2. a signing key for this run only (never written to disk) -----------------------------------
if [ -z "${Jwt__Key:-}" ]; then
  Jwt__Key=$(node -e 'process.stdout.write(require("node:crypto").randomBytes(48).toString("base64url"))')
  export Jwt__Key
  echo "JWT: generated for this run (not written to disk)."
else
  echo "JWT: taken from the environment."
fi

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
# The dev certificate is self-signed; Vite's proxy ignores it (secure:false) and the seed script only
# disables verification for localhost. No production target is ever affected.
export DOTNET_NOLOGO=1

API_PID=""
cleanup() {
  if [ -n "$API_PID" ] && kill -0 "$API_PID" 2>/dev/null; then
    echo ""
    echo "Stopping API (pid $API_PID)..."
    pkill -P "$API_PID" 2>/dev/null || true
    kill "$API_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

# ---- 3. frontend dependencies (only when missing; the lockfile is preserved) ----------------------
if [ ! -d "$ROOT/src/DyeHouseERP.Web/node_modules" ]; then
  echo "Installing frontend dependencies (npm ci)..."
  (cd "$ROOT/src/DyeHouseERP.Web" && npm ci) || fail "npm ci failed."
fi

# ---- 4. the API ---------------------------------------------------------------------------------
# The HTTPS profile needs a development certificate. Create it if missing; trusting it is optional
# because Vite's proxy ignores certificate errors and the seed script only skips verification for
# localhost.
if ! dotnet dev-certs https --check >/dev/null 2>&1; then
  echo "Creating a local HTTPS development certificate..."
  dotnet dev-certs https >/dev/null 2>&1 || echo "WARNING: could not create the dev certificate; the API may fail to bind https."
fi

echo "Starting the API (https://localhost:7100)..."
dotnet run --project "$ROOT/src/DyeHouseERP.API/DyeHouseERP.API.csproj" \
  --launch-profile "DyeHouseERP.API" \
  >"$LOG_DIR/api.log" 2>&1 &
API_PID=$!

READY=0
i=0
while [ "$i" -lt 90 ]; do
  if ! kill -0 "$API_PID" 2>/dev/null; then
    echo "The API exited during startup. Last lines of $LOG_DIR/api.log:" >&2
    tail -n 40 "$LOG_DIR/api.log" >&2
    exit 1
  fi
  if curl -kfsS -o /dev/null "https://localhost:7100/swagger/index.html" 2>/dev/null; then
    READY=1
    break
  fi
  i=$((i + 1))
  sleep 2
done

if [ "$READY" -ne 1 ]; then
  echo "The API did not answer within 180s. Last lines of $LOG_DIR/api.log:" >&2
  tail -n 40 "$LOG_DIR/api.log" >&2
  exit 1
fi
echo "API is up (log: $LOG_DIR/api.log)."

# ---- 5. real stock, posted through the API --------------------------------------------------------
echo ""
echo "Posting real demo stock through the API..."
API_URL="https://localhost:7100" node "$ROOT/tools/warehouse-demo-seed.mjs" || {
  echo "Seeding failed; the API is still running so the error above can be reproduced in Swagger." >&2
  exit 1
}

# ---- 6. the screens ------------------------------------------------------------------------------
echo ""
echo "Starting Vite (http://localhost:5173) - the proxy targets the API above."
echo "Screenshots to take (all figures must match the POSTED block above):"
echo "  1. /warehouse            the warehouse dropdown + overview of WH-RAW"
echo "  2. WH-RAW > الأرصدة       two balance rows, one per receipt message, KG and M in their own columns"
echo "  3. WH-RAW > كارت الصنف    opening 100/50, period -10/-5, closing 90/45"
echo "  4. WH-RAW > الحركات       the receipt and adjustment rows of this warehouse only"
echo "  5. WH-MAT > نظرة عامة     60 KG from the purchase receipt"
echo "  6. تقارير                warehouse balance report with a total per warehouse and a grand total"
echo ""
echo "Press Ctrl+C to stop both processes."
echo ""

(cd "$ROOT/src/DyeHouseERP.Web" && npm run dev)
