#!/usr/bin/env bash
set -euo pipefail

is_github_actions() {
  [ "${GITHUB_ACTIONS:-false}" = "true" ] && [ -n "${GITHUB_STEP_SUMMARY:-}" ]
}

test_filters=$1

cd "$(dirname "$0")/../../BTCPayServer.Tests"
docker compose --version
docker compose -f "docker-compose.altcoins.yml" down -v

# Pull can fail transiently; retry to match the previous CI behavior.
n=0
until [ "$n" -ge 10 ]; do
  docker compose -f "docker-compose.altcoins.yml" pull && break
  n=$((n+1))
  sleep 5
done

docker compose -f "docker-compose.altcoins.yml" build

test_container_args=(
  -e "TEST_FILTERS=${test_filters}"
  -e "GITHUB_ACTIONS=${GITHUB_ACTIONS:-false}"
)

if is_github_actions; then
  test_container_args+=(
    -e "GITHUB_STEP_SUMMARY=/tmp/Artifacts/github-step-summary"
    -e "GITHUB_WORKSPACE=/source"
  )
fi

if docker compose -f "docker-compose.altcoins.yml" run "${test_container_args[@]}" tests; then
  test_exit_code=0
else
  test_exit_code=$?
fi

exit "${test_exit_code}"
