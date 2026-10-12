#!/usr/bin/env sh
#
# Runs the DyeHouseERP test suites against a REAL SQL Server.
#
# The integration suite (33 tests, including the H7 concurrency ones) owns the
# database it is pointed at: CustomWebApplicationFactory does
# EnsureDeletedAsync() + EnsureCreatedAsync() per test class. Never point this
# at a database whose data matters.
#
# Usage (from anywhere inside the repo):
#
#   DYEHOUSE_TEST_CONNECTION='Server=localhost,1433;Database=DyeHouseERP_IntegrationTests;User Id=sa;Password=<pw>;TrustServerCertificate=True;' \
#     sh tools/run-sqlserver-tests.sh
#
# Optional argument: a dotnet --filter expression, e.g. to run only the
# concurrency category:
#
#   sh tools/run-sqlserver-tests.sh "Category=Concurrency"
#
set -eu

cd "$(dirname "$0")/.."

if [ -z "${DYEHOUSE_TEST_CONNECTION:-}" ]; then
    echo "DYEHOUSE_TEST_CONNECTION is not set." >&2
    echo "Set it to a SQL Server test database connection string - the suite drops and" >&2
    echo "recreates that database, so do not point it at real data." >&2
    exit 2
fi

# Never parse or print the connection string; quoted values can contain semicolons.
echo "SQL Server connection: configured (value not displayed)"
echo "WARNING: the configured test database is dropped and recreated by the suite."
echo "Working dir   : $(pwd)"
echo

if [ "$#" -gt 1 ]; then
    echo "Usage: sh tools/run-sqlserver-tests.sh [integration-filter]" >&2
    exit 2
fi

echo "=== build ==="
dotnet build DyeHouseERP.sln

echo
echo "=== unit suite (no SQL Server needed) ==="
dotnet test tests/DyeHouseERP.UnitTests

echo
echo "=== SQL Server integration suite (real sp_getapplock / rowversion / row locks) ==="
if [ "$#" -eq 1 ] && [ -n "$1" ]; then
    dotnet test tests/DyeHouseERP.IntegrationTests --filter "$1"
else
    dotnet test tests/DyeHouseERP.IntegrationTests
fi
