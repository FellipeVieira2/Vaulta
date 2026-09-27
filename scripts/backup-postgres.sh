#!/usr/bin/env bash
set -Eeuo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/lib/production.sh"
[[ $# == 0 ]] || fail 'Usage: ./scripts/backup-postgres.sh'
production_config
require_command aws
require_command gzip
operation_lock
backup_dir=$(mktemp -d "$VAULTA_STATE_DIR/backup.XXXXXX")
trap 'rm -rf -- "$backup_dir"' EXIT
backup_name="$(date -u +%Y%m%dT%H%M%SZ).sql.gz"
# Password stays inside the existing container; no command-line password and no shell tracing.
# Dump locally first: a failed pg_dump must never create a plausible successful object in S3.
"${COMPOSE[@]}" exec -T postgres sh -eu -c \
    'export PGPASSWORD="$POSTGRES_PASSWORD"; exec pg_dump --host=127.0.0.1 --username="$POSTGRES_USER" --dbname="$POSTGRES_DB" --no-owner --no-acl' \
    | gzip > "$backup_dir/$backup_name"
gzip -t "$backup_dir/$backup_name"
aws_role s3 cp "$backup_dir/$backup_name" \
    "s3://vaulta-assets-142767402064-us-east-1/backups/postgres/$backup_name" --sse AES256 --only-show-errors
printf 'Backup uploaded: %s\n' "$backup_name"
