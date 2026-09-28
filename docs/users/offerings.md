# Offerings and Recurring Payments

**Watch the complete overview video:**

[![Subscription complete overview](https://img.youtube.com/vi/33bPg-g9pfE/0.jpg)](https://www.youtube.com/watch?v=33bPg-g9pfE)

The Subscriptions app lets merchants sell products and services through
recurring cryptocurrency payments. Unlike a card processor, BTCPay Server
cannot charge a customer's wallet automatically. Instead, subscribers prepay a
credit balance, and BTCPay Server deducts each period's cost from that balance
to renew the subscription.

Examples include access to a weekly report, recurring support for an open-source
project, hosted BTCPay Server access, or a software-as-a-service product.

## Concepts

- An **offering** is the product or service being sold.
- A **plan** defines a price, billing period, and level of service within an
  offering. Plans can represent tiers such as Free, Starter, Pro, and
  Enterprise.
- **Entitlements** are optional rights granted by a plan, such as usage limits
  or support levels. Your service interprets and enforces entitlements; BTCPay
  Server stores them but does not enforce their meaning.
- A **subscriber** is a customer associated with a plan. The subscriber remains
  active while the plan's payment requirements are met.
- A **plan checkout** lets a new customer join a plan or an existing subscriber
  add credit.
- **Credits** are the subscriber's prepaid balance. A plan's cost is deducted
  when each billing period starts. If enough credit remains, renewal happens
  without another checkout.

Define the entitlements available for the offering:

<img src="https://github.com/user-attachments/assets/03acc862-fedf-49f1-822e-16b59159bd58" alt="Configuring entitlements for an offering">

Assign the relevant entitlements to each plan:

<img src="https://github.com/user-attachments/assets/fbedfc14-72d8-46c5-b2aa-b523dc168cf7" alt="Assigning entitlements to a subscription plan">

## Subscription Flow

1. Create an offering for the product or service.
2. Define one or more plans and, optionally, their entitlements.
3. Share or embed a plan checkout link.
4. The customer selects a plan, provides an email address, and pays or starts a
   trial.
5. The payment adds credit, the plan cost is deducted, and the subscriber
   becomes active.
6. At the next billing period, BTCPay Server renews the subscription if the
   subscriber has enough credit. Otherwise, the subscriber must add credit.

You can also create a checkout for an existing subscriber to add credit for
future renewals.

<img src="https://github.com/user-attachments/assets/92731615-f1a9-4dc4-b1de-c5bd3ec55859" alt="Subscription plan checkout">

## Subscriber Portal

The subscriber portal lets customers review their subscription status,
entitlements, credit balance and history, invoices, and receipts. They can add
credit, change plans, or disable automatic renewal.

Your service grants access to the portal by creating a temporary portal link
for the subscriber. This prevents someone who obtains a permanent public URL
from accessing subscription details.

<img src="https://github.com/user-attachments/assets/8202bded-e4fd-4d98-8579-57e983bb833d" alt="Subscriber portal">

This is similar to services that expose a temporary subscription-management
link from their account page:

<img src="https://github.com/user-attachments/assets/e33bc8fc-58ab-4245-88dd-8e0c743b6385" alt="Example account page linking to subscription management">

## Plan Lifecycle

Plans support these lifecycle options:

- **Optimistic activation** activates the subscriber as soon as payment is
  detected instead of waiting for settlement. If the invoice later fails or is
  cancelled, the subscriber is deactivated.
- A **trial period** grants temporary access before payment is required.
- A **grace period** keeps a subscriber active temporarily when there is not
  enough credit to renew. A payment during the grace period applies
  retroactively from the original renewal date.
- The **recurring type** sets a monthly, quarterly, yearly, or lifetime term. A
  lifetime plan grants permanent access after one payment.

## Email Rules

Configure email rules to notify the subscriber or merchant about events such as
an ending trial, payment due, payment reminders, or an ending grace period.

<img src="https://github.com/user-attachments/assets/229c10d7-111b-4f2c-97d7-1852ed9f177e" alt="Subscription email rules">
