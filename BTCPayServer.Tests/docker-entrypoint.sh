#!/bin/sh
set -e

dotnet test --filter Fast=Fast --no-build
if [[ "$TESTS_RUN_EXTERNAL_INTEGRATION" == "true" ]]; then
    dotnet test --filter ExternalIntegration=ExternalIntegration --no-build -v n
fi
dotnet test --filter Integration=Integration --no-build -v n
