# Invoice metadata

Each invoice can contain a customizable JSON metadata object. Greenfield API
clients can provide it when creating an invoice and replace it when updating an
invoice. BTCPay Server preserves arbitrary properties, but recognizes several
well-known properties in its user interface, search index, reports, receipts,
and BitPay-compatible API.

Do not send customer data merely because the object permits it. Usually an
`orderId` is enough to correlate an invoice with an external system. Keeping
personal data in one system limits its exposure if a BTCPay Server instance or
third-party host is compromised.

## Well-known properties

| Property path | Description |
|---|---|
| `.orderId` | Order ID from an external system. It is indexed and can be used to search and filter invoices. |
| `.orderUrl` | URL for the order in the external system. The invoice details page displays it as a link. |
| `.paymentRequestId` | Associates the invoice with a payment request and displays a link on the invoice details page. |
| `.posData` | Custom JSON object displayed as additional information on the invoice details page. Older integrations may supply a string containing a JSON object. |
| `.receiptData` | Custom JSON object displayed on the customer-facing receipt and the invoice details page. |
| `.buyerName` | Buyer name displayed in invoice details and BitPay-compatible API responses. |
| `.buyerEmail` | Buyer email displayed in invoice details and BitPay-compatible API responses. |
| `.buyerAddress1` | First buyer address line displayed in invoice details and BitPay-compatible API responses. |
| `.buyerAddress2` | Second buyer address line displayed in invoice details and BitPay-compatible API responses. |
| `.buyerCity` | Buyer city displayed in invoice details and BitPay-compatible API responses. |
| `.buyerState` | Buyer state or region displayed in invoice details and BitPay-compatible API responses. |
| `.buyerZip` | Buyer postal code displayed in invoice details and BitPay-compatible API responses. |
| `.buyerCountry` | Buyer country displayed in invoice details and BitPay-compatible API responses. |
| `.buyerPhone` | Buyer phone number displayed in invoice details and BitPay-compatible API responses. |
| `.comment` | Store-user comment displayed on the invoice details page. |
| `.itemDesc` | Product description displayed in invoice details and included in the invoice report. Point of Sale can set it automatically. |
| `.itemCode` | Product code displayed in invoice details, included in the invoice report, and indexed for `itemcode:` searches. Point of Sale can set it automatically. |
| `.physical` | Boolean indicating whether the invoice represents a physical good. It is exposed in invoice details and BitPay-compatible API responses. |
| `.taxIncluded` | Tax amount in the invoice currency. On creation, BTCPay Server rounds it to the currency precision and constrains it between zero and the invoice amount. |
| `.taxOnTip` | Tax amount attributable to a tip. |

All properties are optional. Custom properties that are not interpreted by
BTCPay Server remain available through the API and can appear under
**Additional Information** in invoice details.

## Examples

### Point of Sale product

```json
{
  "orderId": "pos-app_346KRC5BjXXXo8cRFKwTBmdR6ZJ4",
  "itemCode": "green tea",
  "itemDesc": "Green Tea",
  "orderUrl": "https://btcpay.example/apps/346KRC5BjXXXo8cRFKwTBmdR6ZJ4/pos",
  "receiptData": {
    "Title": "Green Tea",
    "Description": "Fresh green tea"
  }
}
```

### Point of Sale cart

```json
{
  "orderId": "pos-app_346KRC5BjXXXo8cRFKwTBmdR6ZJ4",
  "posData": {
    "tip": 0.48,
    "cart": [
      {
        "id": "pu erh",
        "count": 1,
        "price": {
          "type": 2,
          "value": 2,
          "formatted": "$2.00"
        },
        "title": "Pu Erh"
      },
      {
        "id": "rooibos",
        "count": 1,
        "price": {
          "type": 2,
          "value": 1.2,
          "formatted": "$1.20"
        },
        "title": "Rooibos"
      }
    ],
    "total": 3.68,
    "subTotal": 3.2
  },
  "itemDesc": "Tea shop",
  "orderUrl": "https://btcpay.example/apps/346KRC5BjXXXo8cRFKwTBmdR6ZJ4/pos",
  "receiptData": {
    "Tip": "$0.48",
    "Cart": {
      "Pu Erh": "$2.00 x 1 = $2.00",
      "Rooibos": "$1.20 x 1 = $1.20"
    }
  }
}
```

### Point of Sale keypad

```json
{
  "orderId": "pos-app_346KRC5BjXXXo8cRFKwTBmdR6ZJ4",
  "posData": {
    "total": "12.00",
    "subTotal": "12.00"
  },
  "itemDesc": "Tea shop",
  "orderUrl": "https://btcpay.example/apps/346KRC5BjXXXo8cRFKwTBmdR6ZJ4/pos",
  "receiptData": {}
}
```
