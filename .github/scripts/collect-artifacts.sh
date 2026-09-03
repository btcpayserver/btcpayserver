#!/usr/bin/env bash
set -euo pipefail

mkdir -p /tmp/Artifacts
docker run --rm -v "${COMPOSE_PROJECT_NAME}_tests_datadir:/data" -v /tmp/Artifacts:/host alpine sh -c "cp -r /data/. /host/" || true

test_summary=/tmp/Artifacts/github-step-summary
if [ -n "${GITHUB_STEP_SUMMARY:-}" ] && [ -f "${test_summary}" ]; then
  cat "${test_summary}" >> "${GITHUB_STEP_SUMMARY}"
  rm "${test_summary}"
fi
