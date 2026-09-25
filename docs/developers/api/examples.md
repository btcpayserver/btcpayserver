# Greenfield API examples

These examples create a basic invoice. Replace placeholders and use the API reference at `/docs` on your target instance for the complete request and response models. The key needs `btcpay.store.cancreateinvoice`, scoped to the selected store when possible.

## cURL

```bash
BTCPAY_URL="https://your-btcpay.example"
API_KEY="your-api-key"
STORE_ID="your-store-id"

curl --fail-with-body \
  -X POST \
  -H "Authorization: token $API_KEY" \
  -H "Content-Type: application/json" \
  --data '{"amount":"10.00","currency":"USD","metadata":{"orderId":"ORDER-123"}}' \
  "$BTCPAY_URL/api/v1/stores/$STORE_ID/invoices"
```

## Node.js

```js
const btcpayUrl = 'https://your-btcpay.example'
const apiKey = process.env.BTCPAY_API_KEY
const storeId = process.env.BTCPAY_STORE_ID

const response = await fetch(`${btcpayUrl}/api/v1/stores/${storeId}/invoices`, {
  method: 'POST',
  headers: {
    Authorization: `token ${apiKey}`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({
    amount: '10.00',
    currency: 'USD',
    metadata: { orderId: 'ORDER-123' }
  })
})

if (!response.ok) throw new Error(`BTCPay returned ${response.status}: ${await response.text()}`)
const invoice = await response.json()
console.log(invoice.id, invoice.checkoutLink)
```

For webhook signatures, compute HMAC-SHA256 over the exact request bytes with the webhook secret. Compare `sha256=<lowercase hex digest>` to the `BTCPay-Sig` header with a constant-time comparison. Do this before acting on the parsed event.

## PHP

The maintained [BTCPay Server Greenfield PHP client](https://github.com/btcpayserver/btcpayserver-greenfield-php) is available through Composer:

```bash
composer require btcpayserver/btcpayserver-greenfield-php
```

```php
<?php
require __DIR__ . '/vendor/autoload.php';

$client = new BTCPayServer\Client\Invoice(
    'https://your-btcpay.example',
    getenv('BTCPAY_API_KEY')
);

$invoice = $client->createInvoice(
    getenv('BTCPAY_STORE_ID'),
    'USD',
    BTCPayServer\Util\PreciseNumber::parseString('10.00'),
    'ORDER-123'
);

echo $invoice->getCheckoutLink();
```

Check the client's [examples](https://github.com/btcpayserver/btcpayserver-greenfield-php/tree/master/examples) for its current method signatures and webhook helper.

## OpenAPI clients

The merged OpenAPI document is available at `/swagger/v1/swagger.json`. It can seed generated clients, but review generated number handling, authentication, nullable fields, and endpoint coverage. Regenerate deliberately: the document also includes APIs supplied by plugins installed on that instance.
