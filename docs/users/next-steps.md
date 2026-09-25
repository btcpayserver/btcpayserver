# Next Steps

Choose only the features needed for the store:

- Review **Store Settings** for invoice expiration, confirmation policy,
  exchange rates, checkout appearance, and user access.
- Connect a supported shopping cart or service from the
  [integration documentation](https://docs.btcpayserver.org/CustomIntegration/).
- Use the [Greenfield API](https://docs.btcpayserver.org/API/Greenfield/v1/)
  for a custom integration. Create an API key with only the permissions the
  application needs.
- Configure webhooks when another system must react to invoice events. The
  receiving system should validate events and handle retries safely.
- Explore installed plugins such as Point of Sale or Crowdfunding. Availability
  depends on the server operator.
- Learn the store's refund, payout, and reporting workflows before processing
  production payments.

Before going live, make a second small payment end to end, verify the receiving
wallet independently, enable account two-factor authentication, and agree with
the operator who handles backups, updates, and incident response.
