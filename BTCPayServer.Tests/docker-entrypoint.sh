#!/bin/sh
set -e

FILTERS=" "
if [ ! -z "$TEST_FILTERS" ]; then
FILTERS="--filter $TEST_FILTERS"
fi

dotnet test -c ${CONFIGURATION_NAME} $FILTERS --no-build -v n < /dev/null
