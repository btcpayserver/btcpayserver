using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Dapper;
using Microsoft.EntityFrameworkCore;
using static BTCPayServer.Client.Models.InvoicePaymentMethodDataModel;

namespace BTCPayServer.Services.Reporting;

public class PaymentsReportProvider : ReportProvider
{
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    public PaymentsReportProvider(
        ApplicationDbContextFactory dbContextFactory,
        DisplayFormatter displayFormatter,
        InvoiceRepository invoiceRepository,
        BTCPayNetworkProvider btcPayNetworkProvider)
    {
        _btcPayNetworkProvider = btcPayNetworkProvider;
        DbContextFactory = dbContextFactory;
        DisplayFormatter = displayFormatter;
        InvoiceRepository = invoiceRepository;
    }
    public override string Name => "Payments";
    private ApplicationDbContextFactory DbContextFactory { get; }
    private DisplayFormatter DisplayFormatter { get; }
    public InvoiceRepository InvoiceRepository { get; }

    ViewDefinition CreateViewDefinition()
    {
        return new()
        {
            Fields =
            {
                new ("Date", "datetime"),
                new ("InvoiceId", "invoice_id"),
                new ("OrderId", "string"),
                new ("PaymentType", "string"),
                new ("PaymentId", "string"),
                new ("Confirmed", "boolean"),
                new ("Address", "string"),
                new ("Crypto", "string"),
                new ("CryptoAmount", "amount"),
                new ("NetworkFee", "amount"),
                new ("LightningAddress", "string"),
                new ("Currency", "string"),
                new ("CurrencyAmount", "amount"),
                new ("Rate", "amount")
            },
            Charts = 
            {
                new ()
                {
                    Name = "Aggregated crypto amount",
                    Groups = { "Crypto", "PaymentType" },
                    Totals = { "Crypto" },
                    HasGrandTotal = false,
                    Aggregates = { "CryptoAmount" }
                },
                new ()
                {
                    Name = "Aggregated amount",
                    Groups = { "Currency" },
                    Totals = { "Currency" },
                    HasGrandTotal = false,
                    Aggregates = { "CurrencyAmount" }
                },
                new ()
                {
                    Name = "Group by Lightning Address",
                    Filters = { "typeof this.LightningAddress === 'string' && this.Crypto == \"BTC\"" },
                    Groups = { "LightningAddress", "Currency" },
                    Aggregates = { "CurrencyAmount" },
                    HasGrandTotal = true
                },
                new ()
                {
                    Name = "Group by Lightning Address (Crypto)",
                    Filters = { "typeof this.LightningAddress === 'string' && this.Crypto == \"BTC\"" },
                    Groups = { "LightningAddress", "Crypto" },
                    Aggregates = { "CryptoAmount" },
                    HasGrandTotal = true
                }
            }
        };
    }

    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        queryContext.ViewDefinition = CreateViewDefinition();
        await using var ctx = DbContextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();
        var invoices = await InvoiceRepository.GetInvoices(new InvoiceQuery()
        {
            StoreId = [queryContext.StoreId],
            StartDate = queryContext.From,
            EndDate = queryContext.To,
            OrderByDesc = false,
        }, cancellation);
        
        foreach (var invoice in invoices)
        {
            foreach (var payment in invoice.GetPayments(true))
            {
                var values = queryContext.CreateData();
                values.Add(invoice.InvoiceTime);
                values.Add(invoice.Id);
                values.Add(invoice.Metadata.OrderId);
                var paymentId = payment.GetPaymentMethodId();
                bool isLightning = false;
                if (paymentId.PaymentType == PaymentTypes.LightningLike || paymentId.PaymentType == PaymentTypes.LNURLPay)
                {
                    isLightning = true;
                    values.Add("Lightning");
                }
                else if (paymentId.PaymentType == PaymentTypes.BTCLike)
                    values.Add("On-Chain");
                else
                    values.Add(paymentId.ToStringNormalized());
                values.Add(paymentId.ToStringNormalized());
                
                var paymentData = payment.GetCryptoPaymentData();
                if (paymentData is null)
                    continue;

                values.Add(paymentData.PaymentConfirmed(payment, invoice.SpeedPolicy));
                values.Add(paymentData.GetDestination());
                values.Add(paymentId.CryptoCode);

                var cryptoAmount = paymentData.GetValue();

                var divisibility = 8;
                if (_btcPayNetworkProvider.TryGetNetwork<BTCPayNetwork>(paymentId.CryptoCode, out var network))
                {
                    divisibility = network.Divisibility;
                }
                if (isLightning)
                    divisibility += 3;
                values.Add(new FormattedAmount(cryptoAmount, divisibility).ToJObject());
                values.Add(payment.NetworkFee);
                var consumerdLightningAddress = (invoice.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.LNURLPay))?
                    .GetPaymentMethodDetails() as LNURLPayPaymentMethodDetails)?
                    .ConsumedLightningAddress;
                values.Add(consumerdLightningAddress);
                values.Add(invoice.Currency);
                if (invoice.Rates.TryGetValue(paymentId.CryptoCode, out var rate))
                {
                    values.Add(DisplayFormatter.ToFormattedAmount(rate * cryptoAmount, invoice.Currency ?? "USD")); // Currency amount
                    values.Add(DisplayFormatter.ToFormattedAmount(rate, invoice.Currency ?? "USD"));
                }
                else
                {
                    values.Add(null);
                    values.Add(null);
                }

                queryContext.Data.Add(values);
            }
        }
    }
}
