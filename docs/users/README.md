# User Guide

This guide is for people who use a BTCPay Server instance to receive and
manage payments. Server installation, updates, backups, and infrastructure
belong in the [operator guide](../operators/README.md).

Your server administrator controls registration, available payment methods,
plugins, and some wallet features. Ask the administrator when an option in
this guide is unavailable.

## Overview

BTCPay Server creates invoices, presents checkout to customers, and records
payments sent directly to wallets controlled by the merchant. It does not
hold funds as a payment intermediary.

### Main Concepts

- An **account** is your identity on one BTCPay Server instance.
- A **store** contains payment, checkout, rate, user, and integration settings
  for a business or project.
- A **wallet** supplies addresses to a store and tracks its transactions. Each
  store configures its own payment methods.
- An **invoice** fixes an amount and exchange rate for a limited period and
  tracks the resulting payment.

Most pages in the navigation apply to the currently selected store. Features
appear only when your account has permission and the server supports them.
Only server administrators can access **Server Settings**.

### User and Operator Responsibilities

Users create stores, connect wallets, issue invoices, review payments, and
configure integrations. Operators deploy the instance, maintain its database
and network services, make backups, update it, and investigate server-wide
failures.

## Set Up an Account and Store

### Create an Account

1. Open the URL supplied by your server administrator.
2. Select **Register**, enter your email address and password, and submit the
   form.
3. Complete email verification if the server requires it.
4. In your account settings, enable two-factor authentication and store your
   recovery codes safely.

If registration is unavailable, ask the server administrator for access.

### Create a Store

After initial registration, BTCPay Server prompts you to create your first
store. To add another later, open the store selector and choose **Create
Store**.

1. Enter a name that identifies the business or project.
2. Choose the default currency used to price invoices.
3. If offered, review the price source. The recommended source follows the
   selected currency.
4. Select **Create Store**.

Store settings can be changed later. A store cannot receive on-chain payments
until it has a wallet.

## Set Up a Wallet

Select the store, then use **Set up a wallet** on its dashboard or open
**Wallets > Bitcoin**. Choose one of these approaches:

- **Connect an existing wallet** to import public wallet data from supported
  hardware or software wallets. This keeps private keys off the server and is
  the normal choice when an existing wallet should receive the funds.
- **Create a new watch-only wallet** to generate receiving data while erasing
  its private key from the server. Spending requires the recovery seed or an
  external signer.
- **Create a new hot wallet** to keep its private key on the server and spend
  from the BTCPay Server interface. This is convenient but exposes funds if
  either the account or server is compromised.

The server administrator can disable wallet generation for non-administrators.

### Protect the Wallet

1. Verify wallet details and addresses on a trusted device when the selected
   wallet supports it.
2. Record every newly generated recovery seed offline. Anyone with the seed
   can spend the funds.
3. Do not type an existing recovery seed into an internet-connected server.
   Import extended public information instead.
4. Test recovery and signing before accepting significant value.
5. Keep only an amount appropriate to the security of a hot wallet.

Lightning is a separate payment method with different liquidity and backup
requirements. See the [Lightning documentation](https://docs.btcpayserver.org/LightningNetwork/)
before enabling it.

## Receive a First Payment

Create a small manual invoice to verify the store before connecting an
e-commerce system.

1. Select the store and open **Payments > Invoices**.
2. Select **Create Invoice**.
3. Enter an amount and currency. Add an order ID or item description if useful.
4. Keep at least one configured payment method selected, then select **Create**.
5. Open **Checkout** and pay from a separate wallet as a customer would.
6. Return to the invoice and confirm that BTCPay Server detected the payment.

An on-chain payment normally moves through **Processing** while it waits for
the confirmation policy configured by the store, then becomes **Settled**.
Lightning payments settle without on-chain confirmations. Do not fulfill an
order merely because a transaction was broadcast; use the invoice status and
your business's payment policy.

If the payment is not detected, first confirm that the server's Bitcoin node
and NBXplorer are synchronized, the destination shown by the paying wallet
matches checkout, and the transaction was broadcast. A hosted user should
send the invoice ID and transaction ID to the server administrator without
sharing wallet seeds or private keys.

## Next Steps

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
