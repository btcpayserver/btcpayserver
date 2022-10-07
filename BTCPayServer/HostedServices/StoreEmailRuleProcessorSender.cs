using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Events;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using BTCPayServer.Services.Invoices;
using InvoiceData = BTCPayServer.Client.Models.InvoiceData;

namespace BTCPayServer.HostedServices;

public class StoreEmailRuleProcessorSender : EventHostedServiceBase
{
    private readonly StoreRepository _storeRepository;
    private readonly EmailSenderFactory _emailSenderFactory;
    private readonly LinkGenerator _linkGenerator;
    private readonly BTCPayServerEnvironment _environment;

    public StoreEmailRuleProcessorSender(StoreRepository storeRepository, EventAggregator eventAggregator,
        ILogger<InvoiceEventSaverService> logger,
        EmailSenderFactory emailSenderFactory,
        LinkGenerator linkGenerator, BTCPayServerEnvironment environment) : base(
        eventAggregator, logger)
    {
        _storeRepository = storeRepository;
        _emailSenderFactory = emailSenderFactory;
        _linkGenerator = linkGenerator;
        _environment = environment;
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<InvoiceEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is InvoiceEvent invoiceEvent)
        {
            var type = WebhookSender.GetWebhookEvent(invoiceEvent);
            if (type is null)
            {
                return;
            }

            var store = await _storeRepository.FindStore(invoiceEvent.Invoice.StoreId);


            var blob = store.GetStoreBlob();
            if (blob.EmailRules?.Any() is true)
            {
                var actionableRules = blob.EmailRules.Where(rule => rule.Trigger == type.Type).ToList();
                if (actionableRules.Any())
                {
                    var sender = await _emailSenderFactory.GetEmailSender(invoiceEvent.Invoice.StoreId);
                    foreach (UIStoresController.StoreEmailRule actionableRule in actionableRules)
                    {
                        var recipients = (actionableRule.To?.Split(",", StringSplitOptions.RemoveEmptyEntries)??Array.Empty<string>())
                            .Select(o =>
                            {
                                MailboxAddressValidator.TryParse(o, out var mb);
                                return mb;
                            })
                            .Where(o => o != null)
                            .ToList();
                        if (actionableRule.CustomerEmail &&
                            MailboxAddressValidator.TryParse(invoiceEvent.Invoice.Metadata.BuyerEmail, out var bmb))
                        {
                            recipients.Add(bmb);
                        }
                        var i = GreenfieldInvoiceController.ToModel(invoiceEvent.Invoice, _linkGenerator, null);
                        sender.SendEmail(recipients.ToArray(), null, null, Interpolator(actionableRule.Subject, i),
                            Interpolator(actionableRule.Body, i));
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
            .Replace("{Invoice.Price}", i.Amount.ToString(CultureInfo.InvariantCulture))
            .Replace("{Invoice.Currency}", i.Currency)
            .Replace("{Invoice.Status}", i.Status.ToString())
            .Replace("{Invoice.AdditionalStatus}", i.AdditionalStatus.ToString())
            .Replace("{Invoice.OrderId}", i.Metadata.ToObject<InvoiceMetadata>().OrderId);
    }
}
