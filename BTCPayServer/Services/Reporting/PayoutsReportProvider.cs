using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;

namespace BTCPayServer.Services.Reporting;

public class PayoutsReportProvider:ReportProvider
{
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;

    public PayoutsReportProvider(PullPaymentHostedService pullPaymentHostedService, BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings)
    {
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
            if (PaymentMethodId.TryParse(payout.PaymentMethodId, out var paymentType))
            {
                if (paymentType.PaymentType == PaymentTypes.LightningLike || paymentType.PaymentType == PaymentTypes.LNURLPay)
                    data.Add("Lightning");
                else if (paymentType.PaymentType == PaymentTypes.BTCLike)
                    data.Add("On-Chain");
                else
                    data.Add(paymentType.PaymentType.ToStringNormalized());
            }
            else
                continue;
            data.Add(paymentType.CryptoCode);
            data.Add(blob.CryptoAmount);
            var ppBlob = payout.PullPaymentData?.GetBlob();
            data.Add(ppBlob?.Currency??paymentType.CryptoCode);
            data.Add(blob.Amount);
            data.Add(blob.Destination);
            queryContext.Data.Add(data);
        }
        
        
    }

    private ViewDefinition CreateDefinition()
    {
        return new ViewDefinition()
        {
            Fields = new List<StoreReportResponse.Field>()
            {
                new("Date", "datetime"),
                new("Source", "string"),
                new("State", "string"),
                new("PaymentType", "string"),
                new("Crypto", "string"),
                new("CryptoAmount", "decimal"),
                new("Currency", "string"),
                new("CurrencyAmount", "decimal"),
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
                },new ()
                {
                    Name = "Aggregated amount",
                    Groups = { "Currency", "State" },
                    Totals = { "CurrencyAmount" },
                    HasGrandTotal = false,
                    Aggregates = { "CurrencyAmount" }
                },new ()
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
