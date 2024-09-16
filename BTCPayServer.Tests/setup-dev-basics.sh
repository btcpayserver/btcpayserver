#!/bin/bash
USERHOST="btcpay.local"
BASE="https://localhost:14142"
API_BASE="$BASE/api/v1"
PASSWORD="rockstar"

# Ensure we are in the script directory
cd "$(dirname "${BASH_SOURCE}")"

# Create admin user
admin_id=$(curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'email': 'admin@$USERHOST', 'password': '$PASSWORD', 'isAdministrator': true }" \
    "$API_BASE/users" | jq -r '.id')

printf "Admin ID: %s\n" "$admin_id"

# Create unlimited access API key
admin_api_key=$(curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'permissions': ['unrestricted'], 'label': 'Unrestricted' }" \
    --user "admin@$USERHOST:$PASSWORD" \
    "$API_BASE/api-keys" | jq -r '.apiKey')

printf "Admin API Key: %s\n" "$admin_api_key"

printf "\n"

# Create Store Owner
owner_id=$(curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'email': 'owner@$USERHOST', 'password': '$PASSWORD', 'isAdministrator': false }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/users" | jq -r '.id')

printf "Store Owner ID: %s\n" "$owner_id"

# Create Store Manager
manager_id=$(curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'email': 'manager@$USERHOST', 'password': '$PASSWORD', 'isAdministrator': false }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/users" | jq -r '.id')

printf "Store Manager ID: %s\n" "$manager_id"

# Create Store Employee
employee_id=$(curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'email': 'employee@$USERHOST', 'password': '$PASSWORD', 'isAdministrator': false }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/users" | jq -r '.id')

printf "Store Employee ID: %s\n" "$employee_id"

printf "\n"

# Create Satoshis Steaks store
res=$(curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'name': 'Satoshis Steaks', 'checkoutType': 'V2', 'lightningAmountInSatoshi': true, 'onChainWithLnInvoiceFallback': true, 'playSoundOnPayment': true, 'defaultCurrency': 'EUR' }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores")
store_id_satoshis_steaks=$( echo $res | jq -r '.id')
if [ -z "${store_id_satoshis_steaks}" ]; then
    printf "Error creating Satoshis Steaks store: %s\n" "$res"
    exit 1
fi
printf "Satoshis Steaks Store ID: %s\n" "$store_id_satoshis_steaks"

# Create Hot Wallet for Satoshis Steaks store
wallet_enabled_satoshis_steaks=$(curl -s -k -X PUT -H 'Content-Type: application/json' \
    -d "{'enabled': true, 'config': 'tpubDC2mCtL7EPhey3qRgHXmKQRraxXgiuSTkHdJbDW22xLK1YMXy8jdEq7jx2UN5z1wU5xBWWZdSpAobG1bbZBTR4f8R3AjL31EzoexpngKUXM' }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_satoshis_steaks/payment-methods/BTC-CHAIN")

# Create Internal Node connection for Satoshis Steaks store
ln_enabled_satoshis_steaks=$(curl -s -k -X PUT -H 'Content-Type: application/json' \
    -d "{'enabled': true, 'config': { 'connectionString': 'Internal Node' } }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_satoshis_steaks/payment-methods/BTC-LN")

# LNURL settings
curl -s -k -X PUT -H 'Content-Type: application/json' \
    -d "{'enabled': true, 'config': { 'lud12Enabled': true, 'useBech32Scheme': true } }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_satoshis_steaks/payment-methods/BTC-LNURL" >/dev/null 2>&1

# Fund Satoshis Steaks wallet
btcaddress_satoshis_steaks=$(curl -s -k -X GET -H 'Content-Type: application/json' \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_satoshis_steaks/payment-methods/BTC-CHAIN/wallet/address" | jq -r '.address')

./docker-bitcoin-cli.sh sendtoaddress "$btcaddress_satoshis_steaks" 6.15 >/dev/null 2>&1

printf "\n"

# Add store users to Satoshis Steaks store
curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'userId': '$owner_id', 'role': 'Owner' }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_satoshis_steaks/users" >/dev/null 2>&1

curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'userId': '$manager_id', 'role': 'Manager' }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_satoshis_steaks/users" >/dev/null 2>&1

curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'userId': '$employee_id', 'role': 'Employee' }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_satoshis_steaks/users" >/dev/null 2>&1

# Create Nakamoto Nuggets store
store_id_nakamoto_nuggets=$(curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'name': 'Nakamoto Nuggets', 'checkoutType': 'V2', 'lightningAmountInSatoshi': true, 'onChainWithLnInvoiceFallback': true, 'playSoundOnPayment': true }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores" | jq -r '.id')

printf "Nakamoto Nuggets Store ID: %s\n" "$store_id_nakamoto_nuggets"

# Create Hot Wallet for Nakamoto Nuggets store
# Seed: "resist camera spread better amazing cliff giraffe duty betray throw twelve father"
wallet_enabled_nakamoto_nuggets=$(curl -s -k -X PUT -H 'Content-Type: application/json' \
    -d "{'enabled': true, 'config': 'tpubDD79XF4pzhmPSJ9AyUay9YbXAeD1c6nkUqC32pnKARJH6Ja5hGUfGc76V82ahXpsKqN6UcSGXMkzR34aZq4W23C6DAdZFaVrzWqzj24F8BC' }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_nakamoto_nuggets/payment-methods/BTC-CHAIN")

# Connect Nakamoto Nuggets with Merchant LND Lightning node
curl -s -k -X PUT -H 'Content-Type: application/json' \
    -d "{'enabled': true, 'config': { 'connectionString': 'type=lnd-rest;server=http://lnd:lnd@127.0.0.1:35531/;allowinsecure=true' }}" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_nakamoto_nuggets/payment-methods/BTC-LN" >/dev/null 2>&1

# LNURL settings
curl -s -k -X PUT -H 'Content-Type: application/json' \
    -d "{'enabled': true, 'config': { 'lud12Enabled': true, 'useBech32Scheme': true } }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_nakamoto_nuggets/payment-methods/BTC-LNURL" >/dev/null 2>&1

# Add store users to Nakamoto Nuggets store
curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'userId': '$owner_id', 'role': 'Owner' }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_nakamoto_nuggets/users" >/dev/null 2>&1

curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'userId': '$manager_id', 'role': 'Manager' }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_nakamoto_nuggets/users" >/dev/null 2>&1

curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'userId': '$employee_id', 'role': 'Employee' }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_nakamoto_nuggets/users" >/dev/null 2>&1

# Create Nakamoto Nuggets keypad app
keypad_app_id_nakamoto_nuggets=$(curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'appName': 'Keypad', 'title': 'Keypad', 'defaultView': 'light', 'currency': 'SATS' }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/{$store_id_nakamoto_nuggets}/apps/pos" | jq -r '.id')

printf "Nakamoto Nuggets Keypad POS ID: %s\n" "$keypad_app_id_nakamoto_nuggets"

# Create Nakamoto Nuggets cart app
cart_app_id_nakamoto_nuggets=$(curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'appName': 'Cart', 'title': 'Cart', 'defaultView': 'cart', 'template': '[{\"id\":\"birell beer\",\"image\":\"https://i.imgur.com/r8N6rTU.png\",\"priceType\":\"Fixed\",\"price\":\"20\",\"title\":\"Birell Beer\",\"disabled\":false},{\"id\":\"flavoured birell beer\",\"image\":\"https://i.imgur.com/de43iUd.png\",\"priceType\":\"Fixed\",\"price\":\"20\",\"title\":\"Flavoured Birell Beer\",\"disabled\":false},{\"id\":\"wostok\",\"image\":\"https://i.imgur.com/gP6zqub.png\",\"priceType\":\"Fixed\",\"price\":\"25\",\"title\":\"Wostok\",\"disabled\":false},{\"id\":\"pilsner beer\",\"image\":\"https://i.imgur.com/M4EEaEP.png\",\"priceType\":\"Fixed\",\"price\":\"30\",\"title\":\"Pilsner Beer\",\"disabled\":false},{\"id\":\"club mate\",\"image\":\"https://i.imgur.com/H9p9Xwc.png\",\"priceType\":\"Fixed\",\"price\":\"35\",\"title\":\"Club Mate\",\"disabled\":false},{\"id\":\"seicha / selo / koka\",\"image\":\"https://i.imgur.com/ReW3RKe.png\",\"priceType\":\"Fixed\",\"price\":\"35\",\"title\":\"Seicha / Selo / Koka\",\"disabled\":false},{\"id\":\"limonada z kopanic\",\"image\":\"https://i.imgur.com/2Xb35Zs.png\",\"priceType\":\"Fixed\",\"price\":\"40\",\"title\":\"Limonada z Kopanic\",\"disabled\":false},{\"id\":\"mellow drink\",\"image\":\"https://i.imgur.com/ilDUWiP.png\",\"priceType\":\"Fixed\",\"price\":\"40\",\"title\":\"Mellow Drink\",\"disabled\":false},{\"id\":\"bacilli drink\",\"image\":\"https://i.imgur.com/3BsCLgG.png\",\"priceType\":\"Fixed\",\"price\":\"40\",\"title\":\"Bacilli Drink\",\"disabled\":false},{\"description\":\"\",\"id\":\"vincentka\",\"image\":\"https://i.imgur.com/99reAEg.png\",\"priceType\":\"Fixed\",\"price\":\"20\",\"title\":\"Vincentka\",\"disabled\":false,\"index\":\"-1\"},{\"id\":\"kinder bar\",\"image\":\"https://i.imgur.com/va9i6SQ.png\",\"priceType\":\"Fixed\",\"price\":\"20\",\"title\":\"Kinder bar\",\"disabled\":false},{\"id\":\"nutrend bar\",\"image\":\"https://i.imgur.com/zzdIup0.png\",\"priceType\":\"Fixed\",\"price\":\"15\",\"title\":\"Nutrend bar\",\"disabled\":false},{\"id\":\"yoghurt\",\"image\":\"https://i.imgur.com/biP4Dr8.png\",\"priceType\":\"Fixed\",\"price\":\"20\",\"title\":\"Yoghurt\",\"disabled\":false},{\"id\":\"mini magnum\",\"image\":\"https://i.imgur.com/tveN4Aa.png\",\"priceType\":\"Fixed\",\"price\":\"35\",\"title\":\"Mini Magnum\",\"disabled\":false},{\"description\":\"\",\"id\":\"nanuk do:pusy\",\"image\":\"https://i.imgur.com/EzZN6lV.png\",\"priceType\":\"Fixed\",\"price\":\"30\",\"title\":\"Nanuk DO:PUSY\",\"disabled\":false,\"index\":\"-1\"},{\"id\":\"alpro dessert\",\"image\":\"https://i.imgur.com/L0MHkcs.png\",\"priceType\":\"Fixed\",\"price\":\"30\",\"title\":\"Alpro dessert\",\"disabled\":false},{\"id\":\"mixitka bar\",\"image\":\"https://i.imgur.com/gHuTGK3.png\",\"priceType\":\"Fixed\",\"price\":\"30\",\"title\":\"Mixitka bar\",\"disabled\":false},{\"id\":\"instatni polivka\",\"image\":\"https://cdn.rohlik.cz/images/grocery/products/722313/722313-1598298944.jpg\",\"priceType\":\"Fixed\",\"price\":\"15\",\"title\":\"Instatni polivka\",\"disabled\":false},{\"id\":\"m&amp;s instatni polivka\",\"image\":\"https://i.imgur.com/Y8LCJbG.png\",\"priceType\":\"Fixed\",\"price\":\"60\",\"title\":\"M&amp;S instatni polivka\",\"disabled\":false}]' }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/{$store_id_nakamoto_nuggets}/apps/pos" | jq -r '.id')

printf "Nakamoto Nuggets Cart POS ID: %s\n" "$cart_app_id_nakamoto_nuggets"

# Fund Nakamoto Nuggets wallet
btcaddress_nakamoto_nuggets=$(curl -s -k -X GET -H 'Content-Type: application/json' \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_nakamoto_nuggets/payment-methods/BTC-CHAIN/wallet/address" | jq -r '.address')

./docker-bitcoin-cli.sh sendtoaddress "$btcaddress_nakamoto_nuggets" 6.15 >/dev/null 2>&1

printf "\n"

# Create External Lightning based store
store_id_externalln=$(curl -s -k -X POST -H 'Content-Type: application/json' \
    -d "{'name': 'External Lightning (LND)', 'checkoutType': 'V2', 'lightningAmountInSatoshi': true, 'onChainWithLnInvoiceFallback': true }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores" | jq -r '.id')

printf "External Lightning Store ID: %s\n" "$store_id_externalln"

# Connect External Lightning based store with Customer LND Lightning node
curl -s -k -X PUT -H 'Content-Type: application/json' \
    -d "{'enabled': true, 'config': { 'connectionString': 'type=lnd-rest;server=http://lnd:lnd@127.0.0.1:35532/;allowinsecure=true' } }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_externalln/payment-methods/BTC-LN" >/dev/null 2>&1

# LNURL settings
curl -s -k -X PUT -H 'Content-Type: application/json' \
    -d "{'enabled': true, 'config': { 'lud12Enabled': true, 'useBech32Scheme': true } }" \
    -H "Authorization: token $admin_api_key" \
    "$API_BASE/stores/$store_id_externalln/payment-methods/BTC-LNURL" >/dev/null 2>&1

printf "\n"

# Mine some blocks
./docker-bitcoin-generate.sh 5 >/dev/null 2>&1
