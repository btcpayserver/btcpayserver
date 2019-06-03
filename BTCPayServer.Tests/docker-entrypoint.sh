#!/bin/sh
set -e

$FILTERS=" "
if [[ "$TEST_FILTERS" ]]; then
$FILTERS="--filter $TEST_FILTERS"
fi
