using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.LND;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using NBitcoin;
using NBitpayClient;
using Newtonsoft.Json.Linq;
using static BTCPayServer.HostedServices.PullPaymentHostedService.PayoutApproval;

namespace BTCPayServer.Services.Reporting;

public class PaymentsReportProvider : ReportProvider
{

    public PaymentsReportProvider(ApplicationDbContextFactory dbContextFactory, CurrencyNameTable currencyNameTable)
    {
        DbContextFactory = dbContextFactory;
        CurrencyNameTable = currencyNameTable;
    }
    public override string Name => "Payments";
    public ApplicationDbContextFactory DbContextFactory { get; }
    public CurrencyNameTable CurrencyNameTable { get; }

    ViewDefinition CreateViewDefinition()
    {
        return 
            new()
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
                        new ("CryptoAmount", "decimal"),
                        new ("NetworkFee", "decimal"),
                        new ("LightningAddress", "string"),
                        new ("Currency", "string"),
                        new ("CurrencyAmount", "decimal"),
                        new ("Rate", "decimal")
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
                        Name = "Aggregated currency amount",
                        Groups = { "Currency" },
                        Totals = { "Currency" },
                        HasGrandTotal = false,
                        Aggregates = { "CurrencyAmount" }
                    },
                    new ()
                    {
                        Name = "Group by Lightning Address (Currency amount)",
                        Filters = { "typeof this.LightningAddress === 'string' && this.Crypto == \"BTC\"" },
                        Groups = { "LightningAddress", "Currency" },
                        Aggregates = { "CurrencyAmount" },
                        HasGrandTotal = true
                    },
                    new ()
                    {
                        Name = "Group by Lightning Address (Crypto amount)",
                        Filters = { "typeof this.LightningAddress === 'string' && this.Crypto == \"BTC\"" },
                        Groups = { "LightningAddress" },
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
        string[] fields = new[]
        {
                $"i.\"Created\" created",
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
            $"WHERE p.\"Accounted\" IS TRUE AND i.\"Created\" >= @from AND i.\"Created\" < @to AND i.\"StoreDataId\"=@storeId " +
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
            if (PaymentMethodId.TryParse((string)r.payment_type, out var paymentType))
            {
                if (paymentType.PaymentType == PaymentTypes.LightningLike || paymentType.PaymentType == PaymentTypes.LNURLPay)
                    values.Add("Lightning");
                else if (paymentType.PaymentType == PaymentTypes.BTCLike)
                    values.Add("On-Chain");
                else
                    values.Add(paymentType.ToStringNormalized());
            }
            else
                continue;
            values.Add((string)r.payment_id);
            var invoiceBlob = JObject.Parse((string)r.invoice_blob);
            var paymentBlob = JObject.Parse((string)r.payment_blob);


            var data = JObject.Parse(paymentBlob.SelectToken("$.cryptoPaymentData")?.Value<string>()!);
            var conf = data.SelectToken("$.confirmationCount")?.Value<int>();
            values.Add(conf is int o ? o > 0 :
                       paymentType.PaymentType != PaymentTypes.BTCLike ? true : null);
            values.Add(data.SelectToken("$.address")?.Value<string>());
            values.Add(paymentType.CryptoCode);

            decimal cryptoAmount;
            if (data.SelectToken("$.amount")?.Value<long>() is long v)
            {
                cryptoAmount = LightMoney.MilliSatoshis(v).ToDecimal(LightMoneyUnit.BTC);
            }
            else if (data.SelectToken("$.value")?.Value<long>() is long amount)
            {
                cryptoAmount = Money.Satoshis(amount).ToDecimal(MoneyUnit.BTC);
            }
            else
            {
                continue;
            }
            values.Add(cryptoAmount);
            values.Add(paymentBlob.SelectToken("$.networkFee", false)?.Value<decimal>());
            values.Add(invoiceBlob.SelectToken("$.cryptoData.BTC_LNURLPAY.paymentMethod.ConsumedLightningAddress", false)?.Value<string>());
            var currency = invoiceBlob.SelectToken("$.currency")?.Value<string>();
            values.Add(currency);

            values.Add(null); // Currency amount
            var rate = invoiceBlob.SelectToken($"$.cryptoData.{paymentType}.rate")?.Value<decimal>();
            values.Add(rate);
            if (rate is not null)
            {
                values[^2] = (rate.Value * cryptoAmount).RoundToSignificant(CurrencyNameTable.GetCurrencyData(currency ?? "USD", true).Divisibility);
            }

            queryContext.Data.Add(values);
        }
    }
}
