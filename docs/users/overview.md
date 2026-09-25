# BTCPay Server for Users

BTCPay Server creates invoices, presents checkout to customers, and records
payments sent directly to wallets controlled by the merchant. It does not
hold funds as a payment intermediary.

## Main Concepts

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

## User and Operator Responsibilities

Users create stores, connect wallets, issue invoices, review payments, and
configure integrations. Operators deploy the instance, maintain its database
and network services, make backups, update it, and investigate server-wide
failures.

Continue with [account and store setup](./account-and-store-setup.md).
