using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.HostedServices.Webhooks;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices;

public class StoreEmailRuleProcessorSender : EventHostedServiceBase
{
    private readonly StoreRepository _storeRepository;
    private readonly EmailSenderFactory _emailSenderFactory;
    public StoreEmailRuleProcessorSender(StoreRepository storeRepository, EventAggregator eventAggregator,
        ILogger<InvoiceEventSaverService> logger,
        EmailSenderFactory emailSenderFactory) : base(
        eventAggregator, logger)
    {
        _storeRepository = storeRepository;
        _emailSenderFactory = emailSenderFactory;
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

            if (webhookDeliveryRequest.WebhookEvent is not StoreWebhookEvent storeWebhookEvent || storeWebhookEvent.StoreId is null)
            {
                return;
            }
            var store = await _storeRepository.FindStore(storeWebhookEvent.StoreId);
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
                    var sender = await _emailSenderFactory.GetEmailSender(storeWebhookEvent.StoreId);
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
