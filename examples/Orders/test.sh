#!/usr/bin/env bash
# Run the HTTP and persistence contract against a disposable PostgreSQL container.
set -euo pipefail
cd "$(dirname "$0")"
# A separate project keeps the test from touching a database started for manual requests.
export COMPOSE_PROJECT_NAME=derail-orders-test
port=${ORDERS_DB_PORT:-55432}
work=$(mktemp -d)
api_pid=''
cleanup() {
    if [[ -n "$api_pid" ]]; then kill "$api_pid" 2>/dev/null || true; wait "$api_pid" 2>/dev/null || true; fi
    docker compose down --volumes >/dev/null 2>&1 || true
    rm -rf "$work"
}
trap cleanup EXIT
dotnet build -c Release Orders.csproj
docker compose up --detach --wait --force-recreate --renew-anon-volumes
docker compose exec -T postgres psql -X -v ON_ERROR_STOP=1 -c 'INSERT INTO orders (id) VALUES (1), (2)' >/dev/null

DOTNET_ENVIRONMENT=Production ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__Orders="Host=127.0.0.1;Port=$port;Username=sample;Database=orders" \
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
