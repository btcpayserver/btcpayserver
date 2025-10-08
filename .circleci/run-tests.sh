#!/bin/sh
set -e

cd ../BTCPayServer.Tests
docker-compose --version
docker-compose -f "docker-compose.altcoins.yml" down -v

# For some reason, docker-compose pull fails time to time, so we try several times
n=0
until [ "$n" -ge 10 ]
do
   docker-compose -f "docker-compose.altcoins.yml" pull && break
   n=$((n+1))
   sleep 5
done

docker-compose -f "docker-compose.altcoins.yml" build
docker-compose -f "docker-compose.altcoins.yml" run -e "TEST_FILTERS=$1" tests
