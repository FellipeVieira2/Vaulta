#!/usr/bin/env bash
set -Eeuo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/lib/production.sh"
production_config
exec "${COMPOSE[@]}" "$@"
