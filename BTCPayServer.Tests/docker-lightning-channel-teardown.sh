#!/bin/bash
set -e

channels=$(./docker-merchant-lightning-cli.sh listchannels | jq -cr '.channels | map(.short_channel_id) | unique')
printf "Channels: %s\n\r" $channels

for chanid in $(echo "${channels}" | jq -cr '.[]')
do
    printf "Closing channel ID: %s\n\r" $chanid
    ./docker-merchant-lightning-cli.sh close $chanid
    ./docker-bitcoin-generate.sh 20 > /dev/null
done
