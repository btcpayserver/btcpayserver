#!/bin/bash

docker exec -ti btcpayservertests_customer_lightningd_1 lightning-cli "$@"
