#!/usr/bin/env bash
# Runtime smoke test, step 1: ask the running Vite dev server to transform every
# application module. A page that fails to transform (syntax error, missing
# import, bad JSX) returns 500 here, which is the same failure the browser
# would show as a blank page. This does not prove the page renders - only that
# the module graph for it is sound.
set -uo pipefail

BASE="${1:-http://127.0.0.1:5173}"
WEB_DIR="$(cd "$(dirname "$0")/.." && pwd)/src/DyeHouseERP.Web"

total=0
failed=0

check() {
  local path="$1"
  local label="$2"
  total=$((total + 1))
  local out code
  out=$(curl -s -o /tmp/_mod.out -w "%{http_code}" "$BASE$path")
  code="$out"
  if [ "$code" != "200" ]; then
    failed=$((failed + 1))
    echo "  FAIL [$code] $label  ($path)"
    head -c 300 /tmp/_mod.out
    echo
  fi
}

echo "== entry + shell =="
check /src/main.tsx "main.tsx"
check /src/App.tsx "App.tsx"
check /src/index.css "index.css"
check /src/i18n/index.tsx "i18n"
check /src/api/client.ts "api/client.ts"
check /src/api/exports.ts "api/exports.ts"
check /src/api/documents.ts "api/documents.ts"
check /src/permissions.ts "permissions.ts"

echo "== components =="
for f in "$WEB_DIR"/src/components/*.tsx; do
  check "/src/components/$(basename "$f")" "components/$(basename "$f")"
done

echo "== pages =="
for f in "$WEB_DIR"/src/pages/*.tsx; do
  check "/src/pages/$(basename "$f")" "pages/$(basename "$f")"
done
for f in "$WEB_DIR"/src/pages/*/*.tsx; do
  [ -e "$f" ] || continue
  check "/src/pages/$(basename "$(dirname "$f")")/$(basename "$f")" "pages/$(basename "$(dirname "$f")")/$(basename "$f")"
done

echo
echo "modules requested: $total   failed: $failed"
[ "$failed" -eq 0 ]
