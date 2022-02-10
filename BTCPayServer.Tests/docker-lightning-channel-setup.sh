#!/bin/bash

# Commands
BCMD=./docker-bitcoin-cli.sh
GCMD=./docker-bitcoin-generate.sh
C_LN=./docker-customer-lncli.sh
M_LN=./docker-merchant-lncli.sh
C_CL=./docker-customer-lightning-cli.sh
M_CL=./docker-merchant-lightning-cli.sh

function channel_count () {
  local cmd="$1"; local id="$2";
  if [[ $cmd =~ "lightning-cli" ]]; then
    local count=$($cmd listchannels | jq -r ".channels | map(select(.destination == \"$id\" and .active == true)) | length | tonumber") 2>/dev/null
  elif [[ $cmd =~ "lncli" ]]; then
    local count=$($cmd listchannels | jq -r ".channels | map(select(.remote_pubkey == \"$id\" and .active == true)) | length | tonumber") 2>/dev/null
  fi
  return $count
}

function connect () {
  local cmd="$1"; local uri="$2"; local desc="$3";
  local connid=`$cmd connect $uri` 2>/dev/null
  if [[ $connid =~ "already connected" ]]; then
    printf "%s %s\n\r" "✅" "$desc"
  else
    printf "%s %s\n\r" $([[ $uri =~ ^$(echo $connid | jq -r '.id')* ]] && echo "✅" || echo "❌") "$desc"
  fi
}

function create_channel () {
  local cmd="$1"; local id="$2"; local desc="$3"; local opts="$4";
  channel_count "$cmd" "$id"
  local count=$?
  if [[ $count -eq 0 ]]; then
    # fund onchain wallet
    if [[ $cmd =~ "lightning-cli" ]]; then
      local btcaddr=$($cmd newaddr | jq -r '.bech32')
    elif [[ $cmd =~ "lncli" ]]; then
      local btcaddr=$($cmd newaddress p2wkh | jq -r '.address')
    fi
    $BCMD sendtoaddress $btcaddr 0.615 >/dev/null
    $GCMD 10 >/dev/null
    # open channel
    if [[ $cmd =~ "lightning-cli" ]]; then
      $cmd -k fundchannel id=$id amount=5000000 push_msat=2450000 $opts >/dev/null
    elif [[ $cmd =~ "lncli" ]]; then
      $cmd openchannel $id 5000000 2450000 $opts >/dev/null
    fi
    $GCMD 20 >/dev/null
    sleep 1
    channel_count "$cmd" "$id"
    local count=$?
  fi
  printf "%s %s\n\r" $([[ $count -gt 0 ]] && echo "✅" || echo "❌") "$desc"
}

# Nodes
c_cl_info=$($C_CL getinfo | jq '.' 2>/dev/null)
m_cl_info=$($M_CL getinfo | jq '.' 2>/dev/null)
c_cl_id=$(echo $c_cl_info | jq -r '.id')
m_cl_id=$(echo $m_cl_info | jq -r '.id')
c_cl_addr=$(echo $c_cl_info | jq -r '.address[] | "\(.address):\(.port)"')
m_cl_addr=$(echo $m_cl_info | jq -r '.address[] | "\(.address):\(.port)"')
c_cl_uri=$(echo "$c_cl_id@$c_cl_addr")
m_cl_uri=$(echo "$m_cl_id@$m_cl_addr")

c_ln_info=$($C_LN getinfo | jq '.' 2>/dev/null)
m_ln_info=$($M_LN getinfo | jq '.' 2>/dev/null)
c_ln_id=$(echo $c_ln_info | jq -r '.identity_pubkey' 2>/dev/null)
m_ln_id=$(echo $m_ln_info | jq -r '.identity_pubkey' 2>/dev/null)
c_ln_uri=$(echo $c_ln_info | jq -r '.uris[]' 2>/dev/null)
m_ln_uri=$(echo $m_ln_info | jq -r '.uris[]' 2>/dev/null)

printf "\n\rNodes\n\r-----\n\r"
printf "Merchant c-lightning: %s\n\r" $m_cl_uri
printf "Merchant LND:         %s\n\r" $m_ln_uri
printf "Customer c-lightning: %s\n\r" $c_cl_uri
printf "Customer LND:         %s\n\r" $c_ln_uri

# Connections
printf "\n\rConnecting all parties\n\r----------------------\n\r"

connect $M_CL $c_cl_uri "Merchant (c-lightning) to Customer (c-lightning)"
connect $M_CL $c_ln_uri "Merchant (c-lightning) to Customer (LND)"
connect $M_CL $m_ln_uri "Merchant (c-lightning) to Merchant (LND)"
connect $C_CL $m_cl_uri "Customer (c-lightning) to Merchant (c-lightning)"
connect $C_CL $m_ln_uri "Customer (c-lightning) to Merchant (LND)"
connect $C_CL $c_ln_uri "Customer (c-lightning) to Customer (LND)"
connect $M_LN $c_cl_uri "Merchant (LND) to Customer (c-lightning)"
connect $M_LN $c_cl_uri "Merchant (LND) to Customer (c-lightning)"
connect $M_LN $c_ln_uri "Merchant (LND) to Customer (LND)"
connect $C_LN $m_cl_uri "Customer (LND) to Merchant (c-lightning)"
connect $C_LN $c_cl_uri "Customer (LND) to Customer (c-lightning)"
connect $C_LN $m_ln_uri "Customer (LND) to Merchant (LND)"

# Channels
printf "\n\rEstablishing channels\n\r----------------------\n\r"

create_channel $C_CL $m_cl_id "Customer (c-lightning) to Merchant (c-lightning)"
create_channel $C_CL $m_ln_id "Customer (c-lightning) to Merchant (LND)"
create_channel $C_LN $c_cl_id "Customer (LND) to Customer (c-lightning)"
create_channel $M_CL $m_ln_id "Merchant (c-lightning) to Merchant (LND)" "announce=false"
create_channel $C_LN $m_ln_id "Customer (LND) to Merchant (LND)" --private
