#!/usr/bin/env bash
# Shared by operator scripts; never source the secrets file as shell code.
set -Eeuo pipefail
set +x
umask 077
VAULTA_ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$VAULTA_ROOT"
VAULTA_ENV_FILE=${VAULTA_ENV_FILE:-$VAULTA_ROOT/.env.production}
VAULTA_STATE_DIR="$VAULTA_ROOT/.deploy"
ECR_REGISTRY=142767402064.dkr.ecr.us-east-1.amazonaws.com
ECR_REPOSITORY="$ECR_REGISTRY/vaulta-api"

fail() { printf '%s\n' "$*" >&2; exit 1; }
require_command() { command -v "$1" >/dev/null || fail "Required command: $1"; }
valid_tag() { [[ "$1" =~ ^[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,127}$ && "$1" != latest ]]; }

production_config() {
    require_command docker
    require_command python3
    [[ -f "$VAULTA_ENV_FILE" ]] || fail 'Create .env.production from .env.production.example first.'
    python3 - "$VAULTA_ENV_FILE" <<'PY'
import os, stat, sys
mode = stat.S_IMODE(os.stat(sys.argv[1]).st_mode)
if mode & 0o077:
    sys.exit('Secrets file must not be group/world accessible; chmod 600 .env.production.')
PY
    if [[ -z ${IMAGE_TAG:-} && -f "$VAULTA_STATE_DIR/current-tag" ]]; then
        IMAGE_TAG=$(cat "$VAULTA_STATE_DIR/current-tag")
    fi
    valid_tag "${IMAGE_TAG:-}" || fail 'Provide IMAGE_TAG or complete the first deploy; latest is not a deployment tag.'
    export IMAGE_TAG
    COMPOSE=(docker compose --project-name vaulta-production --env-file "$VAULTA_ENV_FILE" -f "$VAULTA_ROOT/docker-compose.production.yml")
    # Validate resolved values without printing config, environment or secrets.
    "${COMPOSE[@]}" config --format json | python3 "$VAULTA_ROOT/scripts/validate-production-config.py"
}

# On EC2, isolate AWS CLI from environment/profile/static credentials. The SDK in the API
# still uses its normal default chain; the compose passes no AWS credential variables/files.
aws_role() {
    env -u AWS_ACCESS_KEY_ID -u AWS_SECRET_ACCESS_KEY -u AWS_SESSION_TOKEN -u AWS_SECURITY_TOKEN \
        -u AWS_PROFILE -u AWS_DEFAULT_PROFILE -u AWS_ROLE_ARN -u AWS_WEB_IDENTITY_TOKEN_FILE \
        -u AWS_CONTAINER_CREDENTIALS_RELATIVE_URI -u AWS_CONTAINER_CREDENTIALS_FULL_URI \
        AWS_SHARED_CREDENTIALS_FILE=/dev/null AWS_CONFIG_FILE=/dev/null \
        AWS_EC2_METADATA_DISABLED=false AWS_EC2_METADATA_V1_DISABLED=true AWS_PAGER='' \
        aws --region us-east-1 "$@"
}

operation_lock() {
    require_command flock
    mkdir -p "$VAULTA_STATE_DIR"
    exec 9>"$VAULTA_STATE_DIR/operation.lock"
    flock -n 9 || fail 'Another deploy or backup is in progress.'
}
