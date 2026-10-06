#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export ASPNETCORE_ENVIRONMENT=Development
export Database__Provider=Sqlite
export Database__Initialize=true
export Database__Seed=true
export Demo__OpenAccess=true
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5080}"
exec dotnet run --project src/CivicPay.Api --no-launch-profile
