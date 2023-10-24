using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices.Webhooks;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using InvoiceData = BTCPayServer.Client.Models.InvoiceData;

namespace BTCPayServer.HostedServices;

public class StoreEmailRuleProcessorSender : EventHostedServiceBase
{
    private readonly StoreRepository _storeRepository;
    private readonly EmailSenderFactory _emailSenderFactory;
    private readonly LinkGenerator _linkGenerator;
    private readonly CurrencyNameTable _currencyNameTable;

    public StoreEmailRuleProcessorSender(StoreRepository storeRepository, EventAggregator eventAggregator,
        ILogger<InvoiceEventSaverService> logger,
        EmailSenderFactory emailSenderFactory,
        LinkGenerator linkGenerator,
        CurrencyNameTable currencyNameTable) : base(
        eventAggregator, logger)
    {
        _storeRepository = storeRepository;
        _emailSenderFactory = emailSenderFactory;
        _linkGenerator = linkGenerator;
        _currencyNameTable = currencyNameTable;
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<WebhookSender.WebhookDeliveryRequest>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is WebhookSender.WebhookDeliveryRequest webhookDeliveryRequest)
        {
            var type = webhookDeliveryRequest.WebhookEvent.Type;
            if (type is null)
            {
                return;
            }

            string storeId = null;
            if (webhookDeliveryRequest.WebhookEvent is WebhookPayoutEvent payoutEvent)
            {
                storeId = payoutEvent?.StoreId;
            }else if (webhookDeliveryRequest.WebhookEvent is WebhookInvoiceEvent webhookInvoiceEvent)
            {
                storeId = webhookInvoiceEvent?.StoreId;
            }

            if (storeId is null)
            {
                return;
            }
            var store = await _storeRepository.FindStore(storeId);
            if (store is null)
            {
                return;
            }

            var blob = store.GetStoreBlob();
            if (blob.EmailRules?.Any() is true)
            {
                var actionableRules = blob.EmailRules.Where(rule => rule.Trigger == type).ToList();
                if (actionableRules.Any())
                {
                    var sender = await _emailSenderFactory.GetEmailSender(storeId);
                    foreach (UIStoresController.StoreEmailRule actionableRule in actionableRules)
                    {
                        var recipients = (actionableRule.To?.Split(",", StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>())
                            .Select(o =>
                            {
                                MailboxAddressValidator.TryParse(o, out var mb);
                                return mb;
                            })
                            .Where(o => o != null)
                            .ToList();
                        
                        var  subject = actionableRule.Subject;
                        var  body = actionableRule.Body;
                        if (webhookDeliveryRequest is InvoiceWebhookDeliveryRequest invoiceWebhookDeliveryRequest)
                        {
                            if (actionableRule.CustomerEmail &&
                                MailboxAddressValidator.TryParse(invoiceWebhookDeliveryRequest.Invoice.Metadata.BuyerEmail, out var bmb))
                            {
                                recipients.Add(bmb);
                            }
                            var i = GreenfieldInvoiceController.ToModel(invoiceWebhookDeliveryRequest.Invoice, _linkGenerator, null);
                            subject = Interpolator(actionableRule.Subject, i);
                            body = Interpolator(actionableRule.Body, i);
                        }
                        sender.SendEmail(recipients.ToArray(), null, null, subject, body);
                    }
                }
            }
        }
    }

    private string Interpolator(string str, InvoiceData i)
    {
        //TODO: we should switch to https://dotnetfiddle.net/MoqJFk later
        return str.Replace("{Invoice.Id}", i.Id)
            .Replace("{Invoice.StoreId}", i.StoreId)
            .Replace("{Invoice.Price}",
                decimal.Round(i.Amount, _currencyNameTable.GetCurrencyData(i.Currency, true).Divisibility,
                    MidpointRounding.ToEven).ToString(CultureInfo.InvariantCulture))
            .Replace("{Invoice.Currency}", i.Currency)
            .Replace("{Invoice.Status}", i.Status.ToString())
            .Replace("{Invoice.AdditionalStatus}", i.AdditionalStatus.ToString())
            .Replace("{Invoice.OrderId}", i.Metadata.ToObject<InvoiceMetadata>().OrderId);
    }
}
