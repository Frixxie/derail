#!/usr/bin/env bash
# Run the HTTP and persistence contract against a fresh local PostgreSQL cluster.
set -euo pipefail
cd "$(dirname "$0")"
export PATH="$(pg_config --bindir):$PATH"
work=$(mktemp -d)
api_pid=''
cleanup() {
    if [[ -n "$api_pid" ]]; then kill "$api_pid" 2>/dev/null || true; wait "$api_pid" 2>/dev/null || true; fi
    pg_ctl -D "$work/db" -m immediate stop >/dev/null 2>&1 || true
    rm -rf "$work"
}
trap cleanup EXIT
dotnet build -c Release Orders.csproj
initdb -D "$work/db" -U sample --auth=trust --no-locale >/dev/null
pg_ctl -D "$work/db" -l "$work/postgres.log" -o "-k '$work' -c listen_addresses=''" -w start >/dev/null
export PGHOST="$work" PGPORT=5432 PGUSER=sample PGDATABASE=postgres
psql -X -v ON_ERROR_STOP=1 -f schema.sql >/dev/null
psql -X -v ON_ERROR_STOP=1 -c 'INSERT INTO orders (id) VALUES (1), (2)' >/dev/null

DOTNET_ENVIRONMENT=Production ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__Orders="Host=$work;Port=5432;Username=sample;Database=postgres" \
    dotnet bin/Release/net11.0/Orders.dll --urls http://127.0.0.1:0 >"$work/api.log" 2>&1 &
api_pid=$!
for _ in {1..100}; do
    url=$(sed -n 's/.*Now listening on: \(http:\/\/127\.0\.0\.1:[0-9]*\).*/\1/p' "$work/api.log")
    [[ -n "$url" ]] && break
    if ! kill -0 "$api_pid" 2>/dev/null; then cat "$work/api.log"; exit 1; fi
    sleep 0.1
done
: "${url:?API did not start}"
python3 test_http.py "$url"
