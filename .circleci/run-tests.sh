#!/bin/sh
set -e

cd ../BTCPayServer.Tests
docker-compose --version
docker-compose -f "docker-compose.altcoins.yml" down -v

NUGET_CACHE=false
docker volume create nuget_datadir
if [ -d "$HOME/.nuget" ]; then
  echo "Populate nuget_datadir from the cache"
  docker run --rm \
    -v nuget_datadir:/target \
    -v "$HOME/.nuget:/source:ro" \
    alpine:latest \
    sh -c 'cp -a /source/. /target/'
  NUGET_CACHE=true
else
  echo "No nuget cache detected"
fi

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

if ! $NUGET_CACHE; then
  rm -rf "$HOME/.nuget"
  docker create --name temp_extract btcpayservertests-tests
  docker cp temp_extract:/root/.nuget "$HOME/.nuget"
  docker rm temp_extract
  echo "Saving the cache in $HOME/.nuget"
    docker run --rm \
      -v nuget_datadir:/source:ro \
      -v "$HOME/.nuget:/target" \
      alpine:latest \
      sh -c 'cp -a /source/. /target/'
fi
