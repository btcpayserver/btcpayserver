#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Plugins.Webhooks.HostedServices;
using BTCPayServer.Plugins.Webhooks.TriggerProviders;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.Webhooks;

public class WebhooksPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Webhooks";
    public override string Identifier => "BTCPayServer.Plugins.Webhooks";
    public override string Name => "Webhooks";
    public override string Description => "Allows you to send webhooks";

    public override void Execute(IServiceCollection services)
    {
        services.AddHostedService<WebhookProviderHostedService>();
        services.AddSingleton<WebhookSender>();
        services.AddSingleton<IHostedService, WebhookSender>(o => o.GetRequiredService<WebhookSender>());
        services.AddScheduledTask<CleanupWebhookDeliveriesTask>(TimeSpan.FromHours(6.0));
        services.AddScheduledTask<DbPeriodicTask>(TimeSpan.FromHours(1.0));

        services.AddHttpClient(WebhookSender.OnionNamedClient)
            .ConfigurePrimaryHttpMessageHandler<Socks5HttpClientHandler>();
        services.AddHttpClient(WebhookSender.LoopbackNamedClient)
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        var userAgent = new System.Net.Http.Headers.ProductInfoHeaderValue("BTCPayServer", BTCPayServerEnvironment.GetInformationalVersion());
        foreach (var clientName in WebhookSender.AllClients.Concat(new[] { BitpayIPNSender.NamedClient }))
        {
            services.AddHttpClient(clientName)
                .ConfigureHttpClient(client =>
                {
                    client.DefaultRequestHeaders.UserAgent.Add(userAgent);
                });
        }

        // Add built in webhooks
        AddInvoiceWebhooks(services);
        AddPayoutWebhooks(services);
        AddPaymentRequestWebhooks(services);
        AddPendingTransactionWebhooks(services);
    }

    private static void AddPendingTransactionWebhooks(IServiceCollection services)
    {
        services.AddWebhookTriggerProvider<PendingTransactionTriggerProvider>();
        var pendingTransactionsPlaceholders = new List<EmailTriggerViewModel.PlaceHolder>()
        {
            new("{PendingTransaction.Id}", "The id of the pending transaction"),
            new("{PendingTransaction.TrimmedId}", "The trimmed id of the pending transaction"),
            new("{PendingTransaction.StoreId}", "The store id of the pending transaction"),
            new("{PendingTransaction.SignaturesCollected}", "The number of signatures collected"),
            new("{PendingTransaction.SignaturesNeeded}", "The number of signatures needed"),
            new("{PendingTransaction.SignaturesTotal}", "The total number of signatures"),
            new("{PendingTransaction.Link}", "The link to the wallet transaction list")
        };

        var pendingTransactionTriggers = new List<EmailTriggerViewModel>()
        {
            new()
            {
                Trigger = PendingTransactionTriggerProvider.PendingTransactionCreated,
                Description = "Pending Transaction - Created",
                DefaultEmail = new()
                {
                    Subject = "Pending Transaction {PendingTransaction.TrimmedId} Created",
                    Body = "Review the transaction {PendingTransaction.Id} and sign it on: {PendingTransaction.Link}"
                },
                PlaceHolders = pendingTransactionsPlaceholders
            },
            new()
            {
                Trigger = PendingTransactionTriggerProvider.PendingTransactionSignatureCollected,
                Description = "Pending Transaction - Signature Collected",
                DefaultEmail = new()
                {
                    Subject = "Signature Collected for Pending Transaction {PendingTransaction.TrimmedId}",
                    Body =
                        "So far {PendingTransaction.SignaturesCollected} signatures collected out of {PendingTransaction.SignaturesNeeded} signatures needed. "
                },
                PlaceHolders = pendingTransactionsPlaceholders
            },
            new()
            {
                Trigger = PendingTransactionTriggerProvider.PendingTransactionBroadcast,
                Description = "Pending Transaction - Broadcast",
                DefaultEmail = new()
                {
                    Subject = "Transaction {PendingTransaction.TrimmedId} has been Broadcast",
                    Body = "Transaction is visible in mempool on: https://mempool.space/tx/{PendingTransaction.Id}. "
                },
                PlaceHolders = pendingTransactionsPlaceholders
            },
            new()
            {
                Trigger = PendingTransactionTriggerProvider.PendingTransactionCancelled,
                Description = "Pending Transaction - Cancelled",
                DefaultEmail = new()
                {
                    Subject = "Pending Transaction {PendingTransaction.TrimmedId} Cancelled",
                    Body = "Transaction {PendingTransaction.Id} is cancelled and signatures are no longer being collected. "
                },
                PlaceHolders = pendingTransactionsPlaceholders
            }
        };

        services.AddSingleton<IDefaultTranslationProvider, WebhooksTranslationProvider>();
        services.AddWebhookTriggerViewModels(pendingTransactionTriggers);
    }

    private static void AddPaymentRequestWebhooks(IServiceCollection services)
    {
        services.AddWebhookTriggerProvider<PaymentRequestTriggerProvider>();
        var paymentRequestPlaceholders = new List<EmailTriggerViewModel.PlaceHolder>()
        {
            new("{PaymentRequest.Id}", "The id of the payment request"),
            new("{PaymentRequest.TrimmedId}", "The trimmed id of the payment request"),
            new("{PaymentRequest.Amount}", "The amount of the payment request"),
            new("{PaymentRequest.Currency}", "The currency of the payment request"),
            new("{PaymentRequest.Title}", "The title of the payment request"),
            new("{PaymentRequest.Description}", "The description of the payment request"),
            new("{PaymentRequest.Link}", "The link to the payment request"),
            new("{PaymentRequest.ReferenceId}", "The reference id of the payment request"),
            new("{PaymentRequest.Status}", "The status of the payment request"),
            new("{PaymentRequest.FormResponse}*", "The form response associated with the payment request")
        };

        var paymentRequestTriggers = new List<EmailTriggerViewModel>()
        {
            new()
            {
                Trigger = WebhookEventType.PaymentRequestCreated,
                Description = "Payment Request - Created",
                DefaultEmail = new()
                {
                    Subject = "Payment Request {PaymentRequest.Id} created",
                    Body = "Payment Request {PaymentRequest.Id} ({PaymentRequest.Title}) created.",
                    CanIncludeCustomerEmail = true
                },
                PlaceHolders = paymentRequestPlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.PaymentRequestUpdated,
                Description = "Payment Request - Updated",
                DefaultEmail = new()
                {
                    Subject = "Payment Request {PaymentRequest.Id} updated",
                    Body = "Payment Request {PaymentRequest.Id} ({PaymentRequest.Title}) updated.",
                    CanIncludeCustomerEmail = true
                },
                PlaceHolders = paymentRequestPlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.PaymentRequestArchived,
                Description = "Payment Request - Archived",
                DefaultEmail = new()
                {
                    Subject = "Payment Request {PaymentRequest.Id} archived",
                    Body = "Payment Request {PaymentRequest.Id} ({PaymentRequest.Title}) archived.",
                    CanIncludeCustomerEmail = true
                },
                PlaceHolders = paymentRequestPlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.PaymentRequestStatusChanged,
                Description = "Payment Request - Status Changed",
                DefaultEmail = new()
                {
                    Subject = "Payment Request {PaymentRequest.Id} status changed",
                    Body = "Payment Request {PaymentRequest.Id} ({PaymentRequest.Title}) status changed to {PaymentRequest.Status}.",
                    CanIncludeCustomerEmail = true
                },
                PlaceHolders = paymentRequestPlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.PaymentRequestCompleted,
                Description = "Payment Request - Completed",
                DefaultEmail = new()
                {
                    Subject = "Payment Request {PaymentRequest.Title} {PaymentRequest.ReferenceId} Completed",
                    Body = "The total of {PaymentRequest.Amount} {PaymentRequest.Currency} has been received and Payment Request {PaymentRequest.Id} is completed.\nReview the payment request: {PaymentRequest.Link}",
                    CanIncludeCustomerEmail = true
                },
                PlaceHolders = paymentRequestPlaceholders
            }
        };
        services.AddWebhookTriggerViewModels(paymentRequestTriggers);
    }

    private static void AddPayoutWebhooks(IServiceCollection services)
    {
        services.AddWebhookTriggerProvider<PayoutTriggerProvider>();
        var payoutPlaceholders = new List<EmailTriggerViewModel.PlaceHolder>()
        {
            new("{Payout.Id}", "The id of the payout"),
            new("{Payout.PullPaymentId}", "The id of the pull payment"),
            new("{Payout.Destination}", "The destination of the payout"),
            new("{Payout.State}", "The current state of the payout"),
            new("{Payout.Metadata}*", "The metadata associated with the payout")
        };
        var payoutTriggers = new List<EmailTriggerViewModel>()
        {
            new()
            {
                Trigger = WebhookEventType.PayoutCreated,
                Description = "Payout - Created",
                DefaultEmail = new()
                {
                    Subject = "Payout {Payout.Id} created",
                    Body = "Payout {Payout.Id} (Pull Payment Id: {Payout.PullPaymentId}) created."
                },
                PlaceHolders = payoutPlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.PayoutApproved,
                Description = "Payout - Approved",
                DefaultEmail = new()
                {
                    Subject = "Payout {Payout.Id} approved",
                    Body = "Payout {Payout.Id} (Pull Payment Id: {Payout.PullPaymentId}) approved."
                },
                PlaceHolders = payoutPlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.PayoutUpdated,
                Description = "Payout - Updated",
                DefaultEmail = new()
                {
                    Subject = "Payout {Payout.Id} updated",
                    Body = "Payout {Payout.Id} (Pull Payment Id: {Payout.PullPaymentId}) updated."
                },
                PlaceHolders = payoutPlaceholders
            }
        };

        services.AddWebhookTriggerViewModels(payoutTriggers);
    }

    private static void AddInvoiceWebhooks(IServiceCollection services)
    {
        services.AddWebhookTriggerProvider<InvoiceTriggerProvider>();
        var invoicePlaceholders = InvoiceTriggerProvider.GetInvoicePlaceholders();
        var emailTriggers = new List<EmailTriggerViewModel>()
        {
            new()
            {
                Trigger = WebhookEventType.InvoiceCreated,
                Description = "Invoice - Created",
                DefaultEmail = new()
                {
                    Subject = "Invoice {Invoice.Id} created",
                    Body = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) created.",
                    CanIncludeCustomerEmail = true
                },
                PlaceHolders = invoicePlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.InvoiceReceivedPayment,
                Description = "Invoice - Received Payment",
                DefaultEmail = new()
                {
                    Subject = "Invoice {Invoice.Id} received payment",
                    Body = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) received payment.",
                    CanIncludeCustomerEmail = true
                },
                PlaceHolders = invoicePlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.InvoiceProcessing,
                Description = "Invoice - Is Processing",
                DefaultEmail = new()
                {
                    Subject = "Invoice {Invoice.Id} processing",
                    Body = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) is processing.",
                    CanIncludeCustomerEmail = true
                },
                PlaceHolders = invoicePlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.InvoiceExpired,
                Description = "Invoice - Expired",
                DefaultEmail = new()
                {
                    Subject = "Invoice {Invoice.Id} expired",
                    Body = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) expired.",
                    CanIncludeCustomerEmail = true
                },
                PlaceHolders = invoicePlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.InvoiceSettled,
                Description = "Invoice - Is Settled",
                DefaultEmail = new()
                {
                    Subject = "Invoice {Invoice.Id} settled",
                    Body = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) is settled.",
                    CanIncludeCustomerEmail = true
                },
                PlaceHolders = invoicePlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.InvoiceInvalid,
                Description = "Invoice - Became Invalid",
                DefaultEmail = new()
                {
                    Subject = "Invoice {Invoice.Id} invalid",
                    Body = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) invalid.",
                    CanIncludeCustomerEmail = true
                },
                PlaceHolders = invoicePlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.InvoicePaymentSettled,
                Description = "Invoice - Payment Settled",
                DefaultEmail = new()
                {
                    Subject = "Invoice {Invoice.Id} payment settled",
                    Body = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) payment settled.",
                    CanIncludeCustomerEmail = true
                },
                PlaceHolders = invoicePlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.InvoiceExpiredPaidPartial,
                Description = "Invoice - Expired Paid Partial",
                DefaultEmail = new()
                {
                    Subject = "Invoice {Invoice.Id} expired with partial payment",
                    Body = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) expired with partial payment. \nPlease review and take appropriate action: {Invoice.Link}",
                    CanIncludeCustomerEmail = true
                },
                PlaceHolders = invoicePlaceholders,

            },
            new()
            {
                Trigger = WebhookEventType.InvoicePaidAfterExpiration,
                Description = "Invoice - Expired Paid Late",
                DefaultEmail = new()
                {
                    Subject = "Invoice {Invoice.Id} paid after expiration",
                    Body = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) paid after expiration.",
                    CanIncludeCustomerEmail = true
                },
                PlaceHolders = invoicePlaceholders
            }
        };
        services.AddWebhookTriggerViewModels(emailTriggers);
    }
}
