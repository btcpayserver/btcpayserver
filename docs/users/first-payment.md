# Receive a First Payment

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

After the test settles, continue with [next steps](./next-steps.md).
