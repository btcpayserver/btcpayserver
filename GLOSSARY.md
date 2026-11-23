# BTCPay Server

BTCPay Server lets merchants receive payments directly and manage incoming and
outgoing payment workflows without holding funds as a payment intermediary.

## Language

### Stores and participants

**Account**:
A person's identity on one BTCPay Server instance, with permissions to access
stores and server features.

**Store**:
The payment, checkout, exchange-rate, user-access, and integration settings for
a business or project.

**Merchant**:
The person or organization operating a store to receive payments.

**Customer**:
A person or organization buying from, donating to, or otherwise paying a store.

**Wallet**:
A store's source of receiving addresses and transaction history.
A wallet's transactions are not necessarily associated with invoices.

### Receiving payments

**Invoice**:
A store's request for payment that fixes an amount and exchange rate for a
limited period and tracks the resulting payments.

**Lightning invoice**:
A request for payment over the Lightning Network, distinct from a BTCPay Server
invoice.

**Payment request**:
A shareable request that collects payments through invoices until its requested
amount is paid or its optional expiration is reached.
Unlike an invoice, it does not fix an exchange rate or receiving address at creation.

**Checkout**:
The customer-facing flow for selecting a payment method and paying an invoice.

**Invoice status**:
An invoice's base payment and settlement state: New, Processing, Settled,
Expired, or Invalid.
Cancellation is an action, not a separate invoice status.

**Payment exception**:
An additional invoice classification, separate from its base status:
Paid partial, Paid over, Paid late, or Marked.

### Outgoing payments

**Recipient**:
A person or organization designated to receive a payout from a store.
A customer receiving a refund is also a recipient.

**Pull payment**:
An authorization for a recipient to claim payments up to a specified limit.
It does not reserve or transfer funds.

**Claim**:
A recipient's submission of an amount and payout destination against a pull
payment, resulting in a payout.

**Payout**:
An outgoing payment requested through a claim, with approval and sending as
separate steps.
Approval alone does not mean funds have been sent.

**Refund**:
An invoice-linked return of funds to a customer, arranged through a pull payment
and the resulting payout.

### Offerings and recurring payments

**Offering**:
A product or service sold through recurring payments, grouping its plans,
entitlements, and subscribers.

**Plan**:
A price, billing period, and level of service within an offering.

**Entitlement**:
An optional right granted by a plan, such as a usage limit or support level.
The merchant's service interprets and enforces that right.

**Subscriber**:
A customer's enrollment in an offering, associated with a current plan and a
credit balance.
A customer can have one enrollment per offering; changing plans preserves it.

**Credits**:
A subscriber's prepaid balance from which the plan's cost is deducted at the
start of each billing period.

**Plan checkout**:
The customer-facing flow for joining a plan or adding credit to an existing
subscriber's balance.
