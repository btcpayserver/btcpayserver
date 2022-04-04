﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
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
                        var dest = actionableRule.To ?? string.Empty;
                        if (actionableRule.CustomerEmail &&
                            !string.IsNullOrEmpty(invoiceEvent.Invoice.Metadata.BuyerEmail))
                        {
                            dest += $",{invoiceEvent.Invoice.Metadata.BuyerEmail}";
                        }

                        actionableRule.To.Split(",", StringSplitOptions.RemoveEmptyEntries);
                        sender.SendEmail(dest, actionableRule.Subject, actionableRule.Body);
                    }
                }
            }
        }
    }
}
