#!/bin/sh
set -e

set --
if [ -n "${TEST_FILTERS:-}" ]; then
set -- --filter "$TEST_FILTERS"
fi

dotnet test -c "${CONFIGURATION_NAME}" "$@" --no-build -v n --output Normal --report-gh --xunit-info --xunit-diagnostics on --long-running 180
