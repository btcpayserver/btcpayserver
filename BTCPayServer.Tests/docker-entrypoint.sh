#!/bin/sh
set -e

dotnet test --filter Fast=Fast --no-build
dotnet test --filter Selenium=Selenium --no-build -v n
dotnet test --filter Integration=Integration --no-build -v n
if [[ "$TESTS_RUN_EXTERNAL_INTEGRATION" == "true" ]]; then
    dotnet test --filter ExternalIntegration=ExternalIntegration --no-build -v n
fi
