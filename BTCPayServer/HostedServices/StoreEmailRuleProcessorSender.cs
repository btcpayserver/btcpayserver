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
                       

                        var request = new SendEmailRequest()
                        {
                            Subject = actionableRule.Subject, Body = actionableRule.Body, Email = actionableRule.To
                        };
                        request = await webhookDeliveryRequest.Interpolate(request, actionableRule);
                        
                      
                        var recipients = (request?.Email?.Split(",", StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>())
                            .Select(o =>
                            {
                                MailboxAddressValidator.TryParse(o, out var mb);
                                return mb;
                            })
                            .Where(o => o != null)
                            .ToArray();
                        
                        if(recipients.Length == 0)
                            continue;
                        
                        sender.SendEmail(recipients.ToArray(), null, null, request.Subject, request.Body);
                    }
                }
            }
        }
    }

}
