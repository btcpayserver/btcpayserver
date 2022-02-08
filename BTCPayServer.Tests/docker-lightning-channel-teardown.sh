#!/bin/bash

./docker-customer-lncli.sh closeallchannels > /dev/null
./docker-merchant-lncli.sh closeallchannels > /dev/null
./docker-bitcoin-generate.sh 10 > /dev/null

channels=$(./docker-merchant-lightning-cli.sh listchannels | jq -cr '.channels | map(.short_channel_id) | unique')

for chanid in $(echo "${channels}" | jq -cr '.[]')
do
    ./docker-merchant-lightning-cli.sh close $chanid > /dev/null
done
./docker-bitcoin-generate.sh 10 > /dev/null

printf "All channels closed!\r\n"
