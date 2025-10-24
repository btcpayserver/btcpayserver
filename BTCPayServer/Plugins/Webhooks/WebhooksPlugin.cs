#nullable  enable
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
        }.AddStoresPlaceHolders();

        var pendingTransactionTriggers = new List<EmailTriggerViewModel>()
        {
            new()
            {
                Trigger = PendingTransactionTriggerProvider.PendingTransactionCreated,
                Description = "Pending Transaction - Created",
                SubjectExample = "Pending Transaction {PendingTransaction.TrimmedId} Created",
                BodyExample = "Review the transaction {PendingTransaction.Id} and sign it on: {PendingTransaction.Link}",
                PlaceHolders = pendingTransactionsPlaceholders
            },
            new()
            {
                Trigger = PendingTransactionTriggerProvider.PendingTransactionSignatureCollected,
                Description = "Pending Transaction - Signature Collected",
                SubjectExample = "Signature Collected for Pending Transaction {PendingTransaction.TrimmedId}",
                BodyExample = "So far {PendingTransaction.SignaturesCollected} signatures collected out of {PendingTransaction.SignaturesNeeded} signatures needed. ",
                PlaceHolders = pendingTransactionsPlaceholders
            },
            new()
            {
                Trigger = PendingTransactionTriggerProvider.PendingTransactionBroadcast,
                Description = "Pending Transaction - Broadcast",
                SubjectExample = "Transaction {PendingTransaction.TrimmedId} has been Broadcast",
                BodyExample = "Transaction is visible in mempool on: https://mempool.space/tx/{PendingTransaction.Id}. ",
                PlaceHolders = pendingTransactionsPlaceholders
            },
            new()
            {
                Trigger = PendingTransactionTriggerProvider.PendingTransactionCancelled,
                Description = "Pending Transaction - Cancelled",
                SubjectExample = "Pending Transaction {PendingTransaction.TrimmedId} Cancelled",
                BodyExample = "Transaction {PendingTransaction.Id} is cancelled and signatures are no longer being collected. ",
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
        }.AddStoresPlaceHolders();

        var paymentRequestTriggers = new List<EmailTriggerViewModel>()
        {
            new()
            {
                Trigger = WebhookEventType.PaymentRequestCreated,
                Description = "Payment Request - Created",
                SubjectExample = "Payment Request {PaymentRequest.Id} created",
                BodyExample = "Payment Request {PaymentRequest.Id} ({PaymentRequest.Title}) created.",
                PlaceHolders = paymentRequestPlaceholders,
                CanIncludeCustomerEmail = true
            },
            new()
            {
                Trigger = WebhookEventType.PaymentRequestUpdated,
                Description = "Payment Request - Updated",
                SubjectExample = "Payment Request {PaymentRequest.Id} updated",
                BodyExample = "Payment Request {PaymentRequest.Id} ({PaymentRequest.Title}) updated.",
                PlaceHolders = paymentRequestPlaceholders,
                CanIncludeCustomerEmail = true
            },
            new()
            {
                Trigger = WebhookEventType.PaymentRequestArchived,
                Description = "Payment Request - Archived",
                SubjectExample = "Payment Request {PaymentRequest.Id} archived",
                BodyExample = "Payment Request {PaymentRequest.Id} ({PaymentRequest.Title}) archived.",
                PlaceHolders = paymentRequestPlaceholders,
                CanIncludeCustomerEmail = true
            },
            new()
            {
                Trigger = WebhookEventType.PaymentRequestStatusChanged,
                Description = "Payment Request - Status Changed",
                SubjectExample = "Payment Request {PaymentRequest.Id} status changed",
                BodyExample = "Payment Request {PaymentRequest.Id} ({PaymentRequest.Title}) status changed to {PaymentRequest.Status}.",
                PlaceHolders = paymentRequestPlaceholders,
                CanIncludeCustomerEmail = true
            },
            new()
            {
                Trigger = WebhookEventType.PaymentRequestCompleted,
                Description = "Payment Request - Completed",
                SubjectExample = "Payment Request {PaymentRequest.Title} {PaymentRequest.ReferenceId} Completed",
                BodyExample = "The total of {PaymentRequest.Amount} {PaymentRequest.Currency} has been received and Payment Request {PaymentRequest.Id} is completed.\nReview the payment request: {PaymentRequest.Link}",
                PlaceHolders = paymentRequestPlaceholders,
                CanIncludeCustomerEmail = true
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
        }.AddStoresPlaceHolders();
        var payoutTriggers = new List<EmailTriggerViewModel>()
        {
            new()
            {
                Trigger = WebhookEventType.PayoutCreated,
                Description = "Payout - Created",
                SubjectExample = "Payout {Payout.Id} created",
                BodyExample = "Payout {Payout.Id} (Pull Payment Id: {Payout.PullPaymentId}) created.",
                PlaceHolders = payoutPlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.PayoutApproved,
                Description = "Payout - Approved",
                SubjectExample = "Payout {Payout.Id} approved",
                BodyExample = "Payout {Payout.Id} (Pull Payment Id: {Payout.PullPaymentId}) approved.",
                PlaceHolders = payoutPlaceholders
            },
            new()
            {
                Trigger = WebhookEventType.PayoutUpdated,
                Description = "Payout - Updated",
                SubjectExample = "Payout {Payout.Id} updated",
                BodyExample = "Payout {Payout.Id} (Pull Payment Id: {Payout.PullPaymentId}) updated.",
                PlaceHolders = payoutPlaceholders
            }
        };

        services.AddWebhookTriggerViewModels(payoutTriggers);
    }

    private static void AddInvoiceWebhooks(IServiceCollection services)
    {
        services.AddWebhookTriggerProvider<InvoiceTriggerProvider>();
        var invoicePlaceholders = new List<EmailTriggerViewModel.PlaceHolder>()
        {
            new("{Invoice.Id}", "The id of the invoice"),
            new("{Invoice.StoreId}", "The id of the store"),
            new("{Invoice.Price}", "The price of the invoice"),
            new("{Invoice.Currency}", "The currency of the invoice"),
            new("{Invoice.Status}", "The current status of the invoice"),
            new("{Invoice.Link}", "The backend link to the invoice"),
            new("{Invoice.AdditionalStatus}", "Additional status information of the invoice"),
            new("{Invoice.OrderId}", "The order id associated with the invoice"),
            new("{Invoice.Metadata}*", "The metadata associated with the invoice")
        }.AddStoresPlaceHolders();
        var emailTriggers = new List<EmailTriggerViewModel>()
        {
            new()
            {
                Trigger = WebhookEventType.InvoiceCreated,
                Description = "Invoice - Created",
                SubjectExample = "Invoice {Invoice.Id} created",
                BodyExample = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) created.",
                PlaceHolders = invoicePlaceholders,
                CanIncludeCustomerEmail = true
            },
            new()
            {
                Trigger = WebhookEventType.InvoiceReceivedPayment,
                Description = "Invoice - Received Payment",
                SubjectExample = "Invoice {Invoice.Id} received payment",
                BodyExample = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) received payment.",
                PlaceHolders = invoicePlaceholders,
                CanIncludeCustomerEmail = true
            },
            new()
            {
                Trigger = WebhookEventType.InvoiceProcessing,
                Description = "Invoice - Is Processing",
                SubjectExample = "Invoice {Invoice.Id} processing",
                BodyExample = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) is processing.",
                PlaceHolders = invoicePlaceholders,
                CanIncludeCustomerEmail = true
            },
            new()
            {
                Trigger = WebhookEventType.InvoiceExpired,
                Description = "Invoice - Expired",
                SubjectExample = "Invoice {Invoice.Id} expired",
                BodyExample = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) expired.",
                PlaceHolders = invoicePlaceholders,
                CanIncludeCustomerEmail = true
            },
            new()
            {
                Trigger = WebhookEventType.InvoiceSettled,
                Description = "Invoice - Is Settled",
                SubjectExample = "Invoice {Invoice.Id} settled",
                BodyExample = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) is settled.",
                PlaceHolders = invoicePlaceholders,
                CanIncludeCustomerEmail = true
            },
            new()
            {
                Trigger = WebhookEventType.InvoiceInvalid,
                Description = "Invoice - Became Invalid",
                SubjectExample = "Invoice {Invoice.Id} invalid",
                BodyExample = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) invalid.",
                PlaceHolders = invoicePlaceholders,
                CanIncludeCustomerEmail = true
            },
            new()
            {
                Trigger = WebhookEventType.InvoicePaymentSettled,
                Description = "Invoice - Payment Settled",
                SubjectExample = "Invoice {Invoice.Id} payment settled",
                BodyExample = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) payment settled.",
                PlaceHolders = invoicePlaceholders,
                CanIncludeCustomerEmail = true
            },
            new()
            {
                Trigger = WebhookEventType.InvoiceExpiredPaidPartial,
                Description = "Invoice - Expired Paid Partial",
                SubjectExample = "Invoice {Invoice.Id} expired with partial payment",
                BodyExample = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) expired with partial payment. \nPlease review and take appropriate action: {Invoice.Link}",
                PlaceHolders = invoicePlaceholders,
                CanIncludeCustomerEmail = true
            },
            new()
            {
                Trigger = WebhookEventType.InvoicePaidAfterExpiration,
                Description = "Invoice - Expired Paid Late",
                SubjectExample = "Invoice {Invoice.Id} paid after expiration",
                BodyExample = "Invoice {Invoice.Id} (Order Id: {Invoice.OrderId}) paid after expiration.",
                PlaceHolders = invoicePlaceholders,
                CanIncludeCustomerEmail = true
            }
        };
        services.AddWebhookTriggerViewModels(emailTriggers);
    }
}
