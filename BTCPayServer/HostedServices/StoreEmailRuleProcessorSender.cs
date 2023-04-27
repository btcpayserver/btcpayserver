using System;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.Events;
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
                        var recipients = (actionableRule.To?.Split(",", StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>())
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
                        var pm = GreenfieldInvoiceController.ToPaymentMethodModels(invoiceEvent.Invoice, true, null);
                        
                        sender.SendEmail(recipients.ToArray(), null, null, Interpolator(actionableRule.Subject, i, pm),
                            Interpolator(actionableRule.Body, i, pm));
                    }
                }
            }
        }
    }

    private string ReplaceMacro<T>(string value, T item, string propName)
    {
        return Regex.Replace(value, @"{(?<exp>[^}]+)}", match => {
            var p = Expression.Parameter(typeof(T), propName);
            var e = System.Linq.Dynamic.Core.DynamicExpressionParser.ParseLambda(new[] { p }, null, match.Groups["exp"].Value);
            return (e.Compile().DynamicInvoke(item) ?? value).ToString();
        });
    }
    private string Interpolator(string str, InvoiceData i,
        InvoicePaymentMethodDataModel[] invoicePaymentMethodDataModels)
    {
        return ReplaceMacro(str, i, "Invoice");
    }
}
