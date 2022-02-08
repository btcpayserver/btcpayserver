#!/bin/bash

container_id="$(docker ps -q --filter label=com.docker.compose.project=btcpayservertests --filter label=com.docker.compose.service=merchant_lnd)"
docker exec -ti $container_id lncli --no-macaroons --rpcserver localhost:10008 "$@"
