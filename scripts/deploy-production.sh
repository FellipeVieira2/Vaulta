#!/usr/bin/env bash
set -Eeuo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/lib/production.sh"
[[ $# == 1 ]] && valid_tag "$1" || fail 'Usage: ./scripts/deploy-production.sh <immutable-image-tag>'
export IMAGE_TAG=$1
production_config
require_command aws
require_command curl
operation_lock
stage=preflight
migration_container=""
ecr_auth_dir=""
cleanup() {
    [[ -z "$migration_container" ]] || docker rm -f "$migration_container" >/dev/null 2>&1 || true
    [[ -z "$ecr_auth_dir" ]] || rm -rf -- "$ecr_auth_dir"
}
failed() {
    local status=$?
    trap - ERR
    printf 'Deploy failed at %s (exit %s). Current successful tag was not changed.\n' "$stage" "$status" >&2
    "${COMPOSE[@]}" ps >&2 || true
    "${COMPOSE[@]}" logs --tail 60 vaulta-api postgres >&2 || true
    exit "$status"
}
trap cleanup EXIT
trap failed ERR
# ECR login tokens are short-lived; keep Docker auth in a private temporary directory only.
ecr_auth_dir=$(mktemp -d)
stage=ecr-login
aws_role ecr get-login-password | docker --config "$ecr_auth_dir" login --username AWS --password-stdin "$ECR_REGISTRY" >/dev/null
stage=pull
docker --config "$ecr_auth_dir" pull "$ECR_REPOSITORY:$IMAGE_TAG"
stage=postgres
# Never recreate an existing database container during a deploy (including failed migrations).
"${COMPOSE[@]}" up -d --no-recreate --wait --wait-timeout 150 postgres
stage=migration
migration_container="vaulta-migrate-$(date -u +%s)-$$"
# Separate container, no service ports, old API remains running throughout this step.
"${COMPOSE[@]}" run --rm --no-deps --name "$migration_container" vaulta-api --migrate
migration_container=""
stage=api
"${COMPOSE[@]}" up -d --no-deps vaulta-api
stage=readiness
ready=false
for ((attempt=0; attempt<60; attempt++)); do
    if curl --fail --silent --max-time 3 http://127.0.0.1:8080/health/ready >/dev/null; then
        ready=true
        break
    fi
    sleep 2
done
[[ "$ready" == true ]]
curl --fail --silent --max-time 3 http://127.0.0.1:8080/health >/dev/null
if [[ -f "$VAULTA_STATE_DIR/current-tag" ]]; then
    previous=$(cat "$VAULTA_STATE_DIR/current-tag")
    if [[ "$previous" != "$IMAGE_TAG" ]]; then printf '%s\n' "$previous" > "$VAULTA_STATE_DIR/previous-tag"; fi
fi
printf '%s\n' "$IMAGE_TAG" > "$VAULTA_STATE_DIR/current-tag.tmp"
mv "$VAULTA_STATE_DIR/current-tag.tmp" "$VAULTA_STATE_DIR/current-tag"
printf 'Deploy healthy: %s\n' "$IMAGE_TAG"
