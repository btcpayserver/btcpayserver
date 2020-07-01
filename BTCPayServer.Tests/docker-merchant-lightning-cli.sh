#!/bin/bash

merchant_lightning_container_id="$(docker ps -q --filter label=com.docker.compose.project=btcpayservertests --filter label=com.docker.compose.service=merchant_lightningd)"
docker exec -ti $merchant_lightning_container_id lightning-cli --rpc-file=/root/.lightning/lightning-rpc "$@"
