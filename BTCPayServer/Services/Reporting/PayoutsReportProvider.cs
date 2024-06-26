#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payouts;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Services.Reporting;

public class PayoutsReportProvider : ReportProvider
{
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;
    private readonly DisplayFormatter _displayFormatter;
    private readonly PayoutMethodHandlerDictionary _handlers;

    public PayoutsReportProvider(
        PullPaymentHostedService pullPaymentHostedService,
        DisplayFormatter displayFormatter,
        PayoutMethodHandlerDictionary handlers,
        BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings)
    {
        _displayFormatter = displayFormatter;
        _handlers = handlers;
        _pullPaymentHostedService = pullPaymentHostedService;
        _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
    }
    
    public override string Name => "Payouts";
    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        queryContext.ViewDefinition = CreateDefinition();
        foreach (var payout in (await _pullPaymentHostedService.GetPayouts(new PullPaymentHostedService.PayoutQuery()
                 {
                     Stores = new[] {queryContext.StoreId},
                     From = queryContext.From,
                     To = queryContext.To,
                     IncludeArchived = true,
                     IncludePullPaymentData = true,


                 })).OrderBy(data => data.Date))
        {
            var blob = payout.GetBlob(_btcPayNetworkJsonSerializerSettings);
            var data = queryContext.CreateData();
            data.Add(payout.Date);
            data.Add(payout.GetPayoutSource(_btcPayNetworkJsonSerializerSettings));
            data.Add(payout.State.ToString());
            string? payoutCurrency;
            if (PayoutMethodId.TryParse(payout.PaymentMethodId, out var pmi))
            {
                var handler = _handlers.TryGet(pmi);
                if (handler is LightningLikePayoutHandler)
                    data.Add("Lightning");
                else if (handler is BitcoinLikePayoutHandler)
                    data.Add("On-Chain");
                else
                    data.Add(pmi.ToString());
                payoutCurrency = handler?.Currency;
            }
            else
                continue;

            var ppBlob = payout.PullPaymentData?.GetBlob();
            var currency = ppBlob?.Currency ?? payoutCurrency;
            if (currency is null)
                continue;
            data.Add(payoutCurrency);
            data.Add(blob.CryptoAmount.HasValue && payoutCurrency is not null ? _displayFormatter.ToFormattedAmount(blob.CryptoAmount.Value, payoutCurrency) : null);
            data.Add(currency);
            data.Add(_displayFormatter.ToFormattedAmount(blob.Amount, currency));
            data.Add(blob.Destination);
            queryContext.Data.Add(data);
        }
    }

    private ViewDefinition CreateDefinition()
    {
        return new ViewDefinition
        {
            Fields = new List<StoreReportResponse.Field>
            {
                new("Date", "datetime"),
                new("Source", "string"),
                new("State", "string"),
                new("PaymentType", "string"),
                new("Crypto", "string"),
                new("CryptoAmount", "amount"),
                new("Currency", "string"),
                new("CurrencyAmount", "amount"),
                new("Destination", "string")
            },
            Charts =
            {
                new ()
                {
                    Name = "Aggregated crypto amount",
                    Groups = { "Crypto", "PaymentType", "State" },
                    Totals = { "Crypto" },
                    HasGrandTotal = false,
                    Aggregates = { "CryptoAmount" }
                },
                new ()
                {
                    Name = "Aggregated amount",
                    Groups = { "Currency", "State" },
                    Totals = { "CurrencyAmount" },
                    HasGrandTotal = false,
                    Aggregates = { "CurrencyAmount" }
                },
                new ()
                {
                    Name = "Aggregated amount by Source",
                    Groups = { "Currency", "State", "Source" },
                    Totals = { "CurrencyAmount" },
                    HasGrandTotal = false,
                    Aggregates = { "CurrencyAmount" }
                }
            }
        };
    }
}
