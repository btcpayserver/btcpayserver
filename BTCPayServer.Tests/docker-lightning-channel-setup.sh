#!/bin/bash

# Commands
BCMD=./docker-bitcoin-cli.sh
GCMD=./docker-bitcoin-generate.sh
CCMD=./docker-customer-lightning-cli.sh
MCMD=./docker-merchant-lightning-cli.sh

function channel_count () {
  local cmd=$1; local id=$2;
  local count=$($cmd listchannels | jq -r ".channels | map(select(.destination == \"$id\")) | length | tonumber") 2>/dev/null
  return $count
}

function create_channel () {
  local cmd=$1; local id=$2;
  local btcaddr=$($cmd newaddr | jq -r '.address')
  $BCMD sendtoaddress $btcaddr 0.15 >/dev/null
  $GCMD 10 >/dev/null
  local fundres=$($cmd fundchannel $id 14500000 5000 | jq -r '.channel_id')
  $GCMD 20 >/dev/null
  sleep 2
  channel_count $cmd $id
  local count=$?
  return $count
}

# General information
cinfo=$($CCMD getinfo | jq '.' 2>/dev/null)
minfo=$($MCMD getinfo | jq '.' 2>/dev/null)
cid=$(echo $cinfo | jq -r '.id')
mid=$(echo $minfo | jq -r '.id')
caddr=$(echo $cinfo | jq -r '.address[] | "\(.address):\(.port)"')
maddr=$(echo $minfo | jq -r '.address[] | "\(.address):\(.port)"')

printf "Customer ID: %s@%s\n\r" $cid $caddr
printf "Merchant ID: %s@%s\n\r" $mid $maddr

# Connections
printf "\n\rConnecting both parties …\n\r"

cconnid=$($CCMD connect "$mid@$maddr" | jq -r '.id' 2>/dev/null)
mconnid=$($MCMD connect "$cid@$caddr" | jq -r '.id' 2>/dev/null)

printf "Customer to merchant %s\n\r" $([[ $cconnid == $mid ]] && echo "succeeded" || echo "failed")
printf "Merchant to customer %s\n\r" $([[ $mconnid == $cid ]] && echo "succeeded" || echo "failed")

# Channels
printf "\n\rChecking channels …\n\r"
channel_count $CCMD $mid
cchanscount=$?
channel_count $MCMD $cid
mchanscount=$?

printf "Customer channel count to merchant: %d\n\r" $cchanscount
printf "Merchant channel count to customer: %d\n\r" $mchanscount

# Open channels if there are none, details: https://github.com/ElementsProject/lightning#opening-a-channel
if [[ $cchanscount -eq 0 ]]; then
  create_channel $CCMD $mid
  cchanres=$?
  printf "Establishing channel from customer to merchant %s\n\r" $([[ $cchanres -gt 0 ]] && echo "succeeded" || echo "failed")
fi

if [[ $mchanscount -eq 0 ]]; then
  create_channel $MCMD $cid
  mchanres=$?
  printf "Establishing channel from merchant to customer %s\n\r" $([[ $mchanres -gt 0 ]] && echo "succeeded" || echo "failed")
fi
