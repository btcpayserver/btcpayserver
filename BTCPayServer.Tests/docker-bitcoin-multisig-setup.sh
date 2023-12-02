#!/bin/bash

# Creates a 2-of-3 multisig setup, following the procedure described here:
# https://github.com/bitcoin/bitcoin/blob/master/doc/multisig-tutorial.md
# https://github.com/bitcoin/bitcoin/blob/master/doc/descriptors.md
#
# Usage:
# ./docker-bitcoin-multisig-setup.sh custom-name
#
# The custom name/prefix is optional and defaults to "multisig".
prefix="${1:-"multi_sig"}"

declare -A xpubs

printf "\n👛 Create descriptor wallets\n\n"
for ((n=1;n<=3;n++)); do
  # Create descriptor wallets, suppress error output in case wallet already exists
  ./docker-bitcoin-cli.sh -named createwallet wallet_name="${prefix}_part_${n}" descriptors=true > /dev/null 2>&1

  # Collect xpubs
  ./docker-bitcoin-cli.sh -rpcwallet="${prefix}_part_${n}" listdescriptors > /dev/null 2>&1
  xpubs["internal_xpub_${n}"]=$(./docker-bitcoin-cli.sh -rpcwallet="${prefix}_part_${n}" listdescriptors | jq '.descriptors | [.[] | select(.desc | startswith("wpkh") and contains("/1/*"))][0] | .desc' | grep -Po '(?<=\().*(?=\))')
  xpubs["external_xpub_${n}"]=$(./docker-bitcoin-cli.sh -rpcwallet="${prefix}_part_${n}" listdescriptors | jq '.descriptors | [.[] | select(.desc | startswith("wpkh") and contains("/0/*"))][0] | .desc' | grep -Po '(?<=\().*(?=\))')
done

for x in "${!xpubs[@]}"; do
  printf "[%s]=%s\n" "$x" "${xpubs[$x]}";
done

external_desc="wsh(sortedmulti(2,${xpubs["external_xpub_1"]},${xpubs["external_xpub_2"]},${xpubs["external_xpub_3"]}))"
internal_desc="wsh(sortedmulti(2,${xpubs["internal_xpub_1"]},${xpubs["internal_xpub_2"]},${xpubs["internal_xpub_3"]}))"

external_desc_sum=$(./docker-bitcoin-cli.sh getdescriptorinfo $external_desc | jq '.descriptor')
internal_desc_sum=$(./docker-bitcoin-cli.sh getdescriptorinfo $internal_desc | jq '.descriptor')

multisig_ext_desc="{\"desc\": $external_desc_sum, \"active\": true, \"internal\": false, \"timestamp\": \"now\"}"
multisig_int_desc="{\"desc\": $internal_desc_sum, \"active\": true, \"internal\": true, \"timestamp\": \"now\"}"

multisig_desc="[$multisig_ext_desc, $multisig_int_desc]"

# Create multisig wallet, suppress error output in case wallet already exists

printf "\n🔐 Create multisig wallet\n"
printf "\nExternal descriptor: $external_desc\n"
printf "\nInternal descriptor: $internal_desc\n"

multisig_name="${prefix}_wallet"
./docker-bitcoin-cli.sh -named createwallet wallet_name="$multisig_name" disable_private_keys=true blank=true descriptors=true > /dev/null 2>&1
./docker-bitcoin-cli.sh -rpcwallet="$multisig_name" importdescriptors "$multisig_desc" > /dev/null 2>&1

# Fund the wallet from the default wallet
printf "\n💰 Fund multisig wallet\n"

newaddress=$(./docker-bitcoin-cli.sh -rpcwallet="$multisig_name" getnewaddress "MultiSig Funding" | tr -d "[:cntrl:]")
txid=$(./docker-bitcoin-cli.sh -rpcwallet="" sendtoaddress "$newaddress" 0.615)
printf "\nReceiving address: $newaddress\n"
printf "\nTransaction ID: $txid\n"

# Confirm everything worked
printf "\nℹ️  Multisig wallet info\n\n"
./docker-bitcoin-cli.sh -rpcwallet="$multisig_name" getwalletinfo

# Unload wallets to prevent having to specify which wallet to use in BTCPay, NBXplorer etc.
for ((n=1;n<=3;n++)); do
  ./docker-bitcoin-cli.sh unloadwallet "${prefix}_part_${n}" > /dev/null 2>&1
done
./docker-bitcoin-cli.sh unloadwallet "$multisig_name" > /dev/null 2>&1
