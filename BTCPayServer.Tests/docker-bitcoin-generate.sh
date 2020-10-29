#!/bin/bash

bitcoind_container_id="$(docker ps -q --filter label=com.docker.compose.project=btcpayservertests --filter label=com.docker.compose.service=bitcoind)"
address=$(docker exec -ti $bitcoind_container_id bitcoin-cli -datadir="/data" getnewaddress)
clean_address="${address//[$'\t\r\n']}"
docker exec $bitcoind_container_id bitcoin-cli -datadir="/data" generatetoaddress "$@" "$clean_address"
