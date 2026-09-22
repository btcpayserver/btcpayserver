#!/bin/sh
set -e

set --
if [ -n "${TEST_FILTERS:-}" ]; then
set -- --filter "$TEST_FILTERS"
fi

dotnet "bin/${CONFIGURATION_NAME}/net10.0/BTCPayServer.Tests.dll" "$@" --output Detailed --show-stdout Failed --show-stderr Failed --progress on --ansi off --report-gh --xunit-diagnostics on --long-running 180
