#!/usr/bin/env bash
# Disposable Docker host only (CI/developer). No AWS calls; validates Production startup against real PostgreSQL.
set -Eeuo pipefail
set +x
umask 077
cd "$(dirname -- "${BASH_SOURCE[0]}")/.."
config_dir=$(mktemp -d)
export IMAGE_TAG=production-smoke
python3 - "$config_dir/env" <<'PY'
import secrets, sys
from pathlib import Path
Path(sys.argv[1]).write_text('POSTGRES_PASSWORD=' + secrets.token_hex(32) + '\nJWT_SECRET=' + secrets.token_hex(32) + '\n')
PY
compose=(docker compose --project-name vaulta-production-smoke --env-file "$config_dir/env" -f docker-compose.production.yml)
cleanup() {
    local status=$?
    if ((status != 0)); then "${compose[@]}" logs --tail 60 >&2 || true; fi
    # Only the isolated, explicitly disposable smoke project is removed.
    "${compose[@]}" down --volumes >/dev/null 2>&1 || true
    rm -rf -- "$config_dir"
}
trap cleanup EXIT
docker tag vaulta-api:test 142767402064.dkr.ecr.us-east-1.amazonaws.com/vaulta-api:production-smoke
"${compose[@]}" config --format json | python3 scripts/validate-production-config.py
"${compose[@]}" up -d --wait --wait-timeout 120 postgres
# Automatic migrations are disabled; migrate before starting the API.
"${compose[@]}" run --rm --no-deps vaulta-api --migrate
# Exercise the actual CLI twice and require tables from every previously omitted module.
"${compose[@]}" run --rm --no-deps vaulta-api --migrate
module_tables=$("${compose[@]}" exec -T postgres sh -c 'exec psql -X -At -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<'SQL'
SELECT count(*) FROM (VALUES
    ('marketplace.listings'), ('orders.orders'), ('payments.payment_transactions'),
    ('wallets.wallets'), ('wallets.wallet_ledger_entries'), ('shipping.shipments'), ('reviews.reviews')
) AS expected(name) WHERE to_regclass(name) IS NOT NULL;
SQL
)
[[ "$module_tables" == 7 ]]
"${compose[@]}" up -d --no-deps vaulta-api
wait_ready() {
    for ((i=0; i<60; i++)); do
        if curl --fail --silent --max-time 3 http://127.0.0.1:8080/health/ready >/dev/null; then return; fi
        sleep 2
    done
    return 1
}
wait_ready
curl --fail --silent http://127.0.0.1:8080/health >/dev/null
# Request with trusted proxy headers reaches auth instead of redirecting again.
[[ $(curl -s -o /dev/null -w '%{http_code}' -H 'X-Forwarded-For: 203.0.113.10' -H 'X-Forwarded-Proto: https' http://127.0.0.1:8080/api/v1/me) == 401 ]]
[[ $(curl -s -o /dev/null -w '%{http_code}' -H 'X-Forwarded-For: 203.0.113.10' -H 'X-Forwarded-Proto: https' http://127.0.0.1:8080/swagger/v1/swagger.json) == 404 ]]
# A marker proves that restarts preserve the database volume.
"${compose[@]}" exec -T postgres sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "CREATE TABLE public.smoke_persistence (id int PRIMARY KEY); INSERT INTO public.smoke_persistence VALUES (1);"' >/dev/null
"${compose[@]}" restart postgres vaulta-api
wait_ready
[[ $("${compose[@]}" exec -T postgres sh -c 'psql -At -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "SELECT count(*) FROM public.smoke_persistence;"') == 1 ]]
"${compose[@]}" exec -T postgres sh -eu -c 'export PGPASSWORD="$POSTGRES_PASSWORD"; exec pg_dump -h 127.0.0.1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --no-acl' | gzip > "$config_dir/backup.sql.gz"
gzip -t "$config_dir/backup.sql.gz"
# Restore into a new disposable database, never overwrite the live database.
"${compose[@]}" exec -T postgres sh -eu -c 'createdb -U "$POSTGRES_USER" vaulta_restore_smoke'
gunzip -c "$config_dir/backup.sql.gz" | "${compose[@]}" exec -T postgres sh -eu -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d vaulta_restore_smoke' >/dev/null
[[ $("${compose[@]}" exec -T postgres sh -c 'psql -At -U "$POSTGRES_USER" -d vaulta_restore_smoke -c "SELECT count(*) FROM public.smoke_persistence;"') == 1 ]]
echo 'PASS: Production migration, health, proxy, private ports/config, persistence, dump and restore (no AWS).'
