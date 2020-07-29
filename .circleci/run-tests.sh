#!/bin/sh
set -e

cd ../BTCPayServer.Tests
docker-compose -v
docker-compose -f "docker-compose.altcoins.yml" down --v
docker-compose -f "docker-compose.altcoins.yml" pull
docker-compose -f "docker-compose.altcoins.yml" build
docker-compose -f "docker-compose.altcoins.yml" run -e "TEST_FILTERS=$1" tests
