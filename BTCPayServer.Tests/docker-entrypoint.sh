#!/bin/sh
set -e

$FILTERS=" "
if [[ "$TEST_FILTERS" ]]; then
$FILTERS="--filter $TEST_FILTERS"
fi

dotnet test $FILTERS --no-build -v n
dotnet test --filter ExternalIntegration=ExternalIntegration --no-build -v n
dotnet test --filter Fast=Fast --no-build
dotnet test --filter Selenium=Selenium --no-build -v n
dotnet test --filter Integration=Integration --no-build -v n
if [[ "$TESTS_RUN_EXTERNAL_INTEGRATION" == "true" ]]; then
    
fi
