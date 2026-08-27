#!/usr/bin/env bash
set -euo pipefail

compose() {
  if command -v docker-compose >/dev/null 2>&1; then
    docker-compose "$@"
  else
    docker compose "$@"
  fi
}

echo "Checking if it is possible to build Bitcoin only..."
cd "$(dirname "$0")/../../BTCPayServer.Tests"
compose -f "docker-compose.yml" build
