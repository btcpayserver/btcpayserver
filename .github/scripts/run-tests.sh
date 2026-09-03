#!/usr/bin/env bash
set -euo pipefail

compose() {
  if command -v docker-compose >/dev/null 2>&1; then
    docker-compose "$@"
  else
    docker compose "$@"
  fi
}

cd "$(dirname "$0")/../../BTCPayServer.Tests"
compose --version
compose -f "docker-compose.altcoins.yml" down -v

# Pull can fail transiently; retry to match the previous CI behavior.
n=0
until [ "$n" -ge 10 ]; do
  compose -f "docker-compose.altcoins.yml" pull && break
  n=$((n+1))
  sleep 5
done

compose -f "docker-compose.altcoins.yml" build

test_container_args=(
  -e "TEST_FILTERS=$1"
  -e "GITHUB_ACTIONS=${GITHUB_ACTIONS:-false}"
)

if [ "${GITHUB_ACTIONS:-false}" = "true" ] && [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  test_container_args+=(
    -e "GITHUB_STEP_SUMMARY=/tmp/github-step-summary"
    -e "GITHUB_WORKSPACE=/source"
    -v "${GITHUB_STEP_SUMMARY}:/tmp/github-step-summary"
  )
fi

compose -f "docker-compose.altcoins.yml" run "${test_container_args[@]}" tests
