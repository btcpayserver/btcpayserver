#!/bin/sh
set -e

FILTERS=" "
if [ ! -z "$TEST_FILTERS" ]; then
FILTERS="--filter $TEST_FILTERS"
fi

dotnet test -c ${CONFIGURATION_NAME} $FILTERS --no-build -v n --logger "console;verbosity=normal" < /dev/null
:bc1q4k4zlga72f0t0jrsyh93dzv2k7upry6an304jp