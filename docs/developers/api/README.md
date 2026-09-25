# Greenfield API integrations

The Greenfield API is BTCPay Server's versioned REST API. Use the interactive reference at `/docs` on the instance you integrate with; it reflects that instance and includes endpoints contributed by installed plugins. Its OpenAPI document is at `/swagger/v1/swagger.json`.

## Before you start

You need:

- The base URL of the user's BTCPay Server instance, without assuming a particular host.
- A store ID for store-scoped operations.
- An API key restricted to the permissions and stores your integration needs.

Create a key manually under **Account > Manage account > API keys** while prototyping. For a third-party integration, use the [interactive authorization flow](authentication.md#interactive-authorization) so users do not give your application their password.

## Make a first request

This request reads one store. The API reference for each endpoint lists its required permission.

```bash
BTCPAY_URL="https://your-btcpay.example"
API_KEY="your-api-key"
STORE_ID="your-store-id"

curl --fail-with-body \
  -H "Authorization: token $API_KEY" \
  "$BTCPAY_URL/api/v1/stores/$STORE_ID"
```

Use `Content-Type: application/json` when sending JSON. Treat non-2xx responses as failures and log the status and response body without logging credentials.

## Typical payment integration

1. Ask the user for their BTCPay Server URL.
2. Send them through interactive authorization with only the permissions you need and store scope enabled.
3. Create an invoice from your backend and store the returned invoice ID with your order.
4. Redirect the customer to the returned `checkoutLink`.
5. Register a webhook, retain its secret securely, and verify every delivery against the raw request body.
6. Make webhook processing idempotent and use invoice `status` plus `additionalStatus` when reconciling state.
7. Issue refunds through the invoice refund endpoint when required.

Do not place data on an invoice merely because the model permits it. Usually an order ID is enough to correlate records; keeping customer data in one system limits exposure.

## Integration practices

- Discover operations and permissions from the target instance's `/docs`; deployments and plugin sets differ.
- Keep API keys server-side, encrypt them at rest, and never place them in browser code or URLs.
- Scope keys to selected stores and least privilege. Plan for revocation and reconnection.
- Verify webhook signatures before parsing business data. Preserve the exact raw body for HMAC verification.
- Acknowledge valid webhook deliveries promptly, process retries safely, and reconcile through `GET` when events may have been missed.
- Test partial, overpaid, late, expired, invalid, and settled invoice states rather than treating “a payment was seen” as final settlement.
- Expect network failures and rate limiting. Retry safe reads with bounded backoff; use application-level idempotency before retrying writes.

See [authentication](authentication.md), [language examples](examples.md), and the [Greenfield API reference](https://docs.btcpayserver.org/API/Greenfield/v1/).
