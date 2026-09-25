# Set Up a Wallet

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

## Protect the Wallet

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

When the wallet reports ready, [receive a test payment](./first-payment.md).
