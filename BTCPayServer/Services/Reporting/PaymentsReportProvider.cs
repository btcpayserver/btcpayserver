using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services.Reporting;

public class PaymentsReportProvider : ReportProvider
{
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    public PaymentsReportProvider(
        ApplicationDbContextFactory dbContextFactory,
        DisplayFormatter displayFormatter,
        BTCPayNetworkProvider btcPayNetworkProvider)
    {
        _btcPayNetworkProvider = btcPayNetworkProvider;
        DbContextFactory = dbContextFactory;
        DisplayFormatter = displayFormatter;
    }
    public override string Name => "Payments";
    private ApplicationDbContextFactory DbContextFactory { get; }
    private DisplayFormatter DisplayFormatter { get; }

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
        string[] fields =
        {
                "i.\"Created\" created",
                "i.\"Id\" invoice_id",
                "i.\"OrderId\" order_id",
                "p.\"Id\" payment_id",
                "p.\"Type\" payment_type",
                "i.\"Blob2\" invoice_blob",
                "p.\"Blob2\" payment_blob",
            };
        string select = "SELECT " + String.Join(", ", fields) + " ";
        string body =
            "FROM \"Payments\" p " +
            "JOIN \"Invoices\" i ON i.\"Id\" = p.\"InvoiceDataId\" " +
            "WHERE p.\"Accounted\" IS TRUE AND i.\"Created\" >= @from AND i.\"Created\" < @to AND i.\"StoreDataId\"=@storeId " +
            "ORDER BY i.\"Created\"";
        var command = new CommandDefinition(
            commandText: select + body,
            parameters: new
            {
                storeId = queryContext.StoreId,
                from = queryContext.From,
                to = queryContext.To
            },
            cancellationToken: cancellation);
        var rows = await conn.QueryAsync(command);
        foreach (var r in rows)
        {
            var values = queryContext.CreateData();
            values.Add((DateTime)r.created);
            values.Add((string)r.invoice_id);
            values.Add((string)r.order_id);
            bool isLightning = false;
            if (PaymentMethodId.TryParse((string)r.payment_type, out var paymentType))
            {
                if (paymentType.PaymentType == PaymentTypes.LightningLike || paymentType.PaymentType == PaymentTypes.LNURLPay)
                {
                    isLightning = true;
                    values.Add("Lightning");
                }
                else if (paymentType.PaymentType == PaymentTypes.BTCLike)
                    values.Add("On-Chain");
                else
                    values.Add(paymentType.ToStringNormalized());
            }
            else
                continue;
            values.Add((string)r.payment_id);
            //var invoiceBlob = JObject.Parse((string)r.invoice_blob);
            //var paymentBlob = JObject.Parse((string)r.payment_blob);

            var pd = new PaymentData()
            {
                Blob2 = r.payment_blob,
                Accounted = true,
                Type = paymentType.ToStringNormalized()
            };
            var paymentEntity = pd.GetBlob(_btcPayNetworkProvider);
            var paymentData = paymentEntity?.GetCryptoPaymentData();
            if (paymentData is null)
                continue;

            Data.InvoiceData invoiceData = new()
            {
                Blob2 = r.invoice_blob
            };
            var invoiceBlob = invoiceData.GetBlob(_btcPayNetworkProvider);
            invoiceBlob.UpdateTotals();

            values.Add(paymentData.PaymentConfirmed(paymentEntity, SpeedPolicy.MediumSpeed));
            values.Add(paymentData.GetDestination());
            values.Add(paymentType.CryptoCode);

            var cryptoAmount = paymentData.GetValue();

            var divisibility = 8;
            if (_btcPayNetworkProvider.TryGetNetwork<BTCPayNetwork>(paymentType.CryptoCode, out var network))
            {
                divisibility = network.Divisibility;
            }
            if (isLightning)
                divisibility += 3;
            values.Add(new FormattedAmount(cryptoAmount, divisibility).ToJObject());
            values.Add(paymentEntity.NetworkFee);
            var consumerdLightningAddress = (invoiceBlob.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.LNURLPay))?
                .GetPaymentMethodDetails() as LNURLPayPaymentMethodDetails)?
                .ConsumedLightningAddress;
            values.Add(consumerdLightningAddress);
            values.Add(invoiceBlob.Currency);
            if (invoiceBlob.Rates.TryGetValue(paymentType.CryptoCode, out var rate))
            {
                values.Add(DisplayFormatter.ToFormattedAmount(rate * cryptoAmount, invoiceBlob.Currency ?? "USD")); // Currency amount
                values.Add(DisplayFormatter.ToFormattedAmount(rate, invoiceBlob.Currency ?? "USD"));
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
