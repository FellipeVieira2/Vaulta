#!/usr/bin/env bash
set -Eeuo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/lib/production.sh"
export IMAGE_TAG=$(cat .deploy/vision-worker-tag)
valid_tag "$IMAGE_TAG" || fail 'A validated immutable worker tag is required.'
production_config
name=vaulta-vision-follow
override=$(mktemp)
trap 'rm -f -- "$override"' EXIT
trap 'docker stop -t 30 vaulta-vision-follow >/dev/null 2>&1 || true; exit 0' TERM INT
cat > "$override" <<'YAML'
services:
  vaulta-api:
    cpus: 0.25
    mem_limit: 512m
    memswap_limit: 1024m
YAML
if docker inspect "$name" >/dev/null 2>&1; then
    if [[ $(docker inspect -f '{{.State.Running}}' "$name") != true ]]; then
        docker rm "$name" >/dev/null
    else
        expected="$ECR_REPOSITORY:$IMAGE_TAG|[\"--vision-index-follow\",\"/models/clip-base/manifest.json\",\"100\"]|250000000|536870912|1073741824"
        actual=$(docker inspect -f '{{.Config.Image}}|{{json .Config.Cmd}}|{{.HostConfig.NanoCpus}}|{{.HostConfig.Memory}}|{{.HostConfig.MemorySwap}}' "$name")
        [[ "$actual" == "$expected" ]] || fail 'Existing Vision worker configuration differs. Stop its service before updating; active worker was preserved.'
    fi
fi
if ! docker inspect "$name" >/dev/null 2>&1; then
    "${COMPOSE[@]}" -f "$override" run -d --pull never --no-deps --name "$name" \
        -e Vision__ModelManifestPath=/models/clip-base/manifest.json \
        -e Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning \
        -e Logging__LogLevel__System.Net.Http.HttpClient=Warning \
        vaulta-api --vision-index-follow /models/clip-base/manifest.json 100 >/dev/null
fi
code=$(docker wait "$name")
exit "$code"
