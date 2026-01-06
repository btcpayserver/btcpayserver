#!/bin/sh
set -e

cd ../BTCPayServer.Tests

# Run UpdateDefaultTranslations test
docker-compose -f "docker-compose.yml" run -e "TEST_FILTERS=FullyQualifiedName~UpdateDefaultTranslations" tests

# Check if any files were modified
cd ..
if ! git diff --exit-code BTCPayServer/Services/Translations.Default.cs; then
    echo "ERROR: Translations.Default.cs is out of date! Please run UpdateDefaultTranslations test before building docker images."
    exit 1
fi

