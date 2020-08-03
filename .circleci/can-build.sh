#!/bin/sh
set -e

echo "Checking if it is possible to build Bitcoin only..."
cd ../BTCPayServer.Tests
docker-compose -f "docker-compose.yml" build