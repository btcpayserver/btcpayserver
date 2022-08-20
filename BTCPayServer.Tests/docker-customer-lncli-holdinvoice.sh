#!/bin/bash

PREIMAGE=$(cat /dev/urandom | tr -dc 'a-f0-9' | fold -w 64 | head -n 1)
HASH=`node -e "console.log(require('crypto').createHash('sha256').update(Buffer.from('$PREIMAGE', 'hex')).digest('hex'))"`
PAYREQ=$(./docker-customer-lncli.sh addholdinvoice $HASH $@  | jq -r ".payment_request")

echo "HASH:     $HASH"
echo "PREIMAGE: $PREIMAGE"
echo "PAY REQ:  $PAYREQ"
echo ""
echo "SETTLE:   ./docker-customer-lncli.sh settleinvoice $PREIMAGE"
echo "CANCEL:   ./docker-customer-lncli.sh cancelinvoice $HASH"
echo "LOOKUP:   ./docker-customer-lncli.sh lookupinvoice $HASH"
echo ""
echo "TRACK:    ./docker-merchant-lncli.sh trackpayment $HASH"
echo "PAY:      ./docker-merchant-lncli.sh payinvoice $PAYREQ"
