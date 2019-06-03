#!/bin/sh
set -e

cd ../BTCPayServer.Tests
docker-compose -v
docker-compose down --v
docker-compose build
docker-compose run -e "TEST_FILTERS=$1" tests
