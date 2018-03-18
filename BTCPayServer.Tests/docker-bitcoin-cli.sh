#!/bin/bash

docker exec -ti btcpayservertests_bitcoind_1 bitcoin-cli -datadir="/data" "$@"
