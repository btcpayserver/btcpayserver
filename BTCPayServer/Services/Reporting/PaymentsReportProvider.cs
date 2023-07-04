using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using NBitcoin;
using Newtonsoft.Json.Linq;
using static BTCPayServer.HostedServices.PullPaymentHostedService.PayoutApproval;

namespace BTCPayServer.Services.Reporting;

public class PaymentsReportProvider : ReportProvider
{
    public PaymentsReportProvider(ApplicationDbContextFactory dbContextFactory)
    {
        DbContextFactory = dbContextFactory;
    }

    public ApplicationDbContextFactory DbContextFactory { get; }

    public override ViewDefinition CreateViewDefinition()
    {
        return new()
        {
            Name = "Payments",
            Fields =
            {
                    new ("Date", "datetime"),
                    new ("InvoiceId", "invoice_id"),
                    new ("OrderId", "string"),
                    new ("Status", "string"),
                    new ("ExceptionStatus", "string"),
                    new ("PaymentId", "string"),
                    new ("PaymentType", "string"),
                    new ("Confirmed", "boolean"),
                    new ("Address", "string"),
                    new ("Amount", "decimal"),
                    new ("NetworkFee", "decimal"),
                    new ("LightningAddress", "string")
            }
        };
    }


    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();
        string[] fields = new[]
        {
                $"i.\"Created\" created",
                "i.\"Id\" invoice_id",
                "i.\"OrderId\" order_id",
                "i.\"Status\" status",
                "i.\"ExceptionStatus\" exception_status",
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
            var values = queryContext.AddData();
            values.Add((DateTime)r.created);
            values.Add((string)r.invoice_id);
            values.Add((string)r.order_id);
            var status = new InvoiceState((string)r.status, (string)r.exception_status);
            values.Add(status.Status.ToModernStatus().ToString());
            values.Add(status.ExceptionStatus.ToString());

            if (PaymentMethodId.TryParse((string)r.payment_id, out var paymentId))
            {
                values.Add(paymentId.ToStringNormalized());
            }
            else
            {
                values.Add((string)r.payment_id);
            }
            values.Add((string)r.payment_type);
            var invoiceBlob = JObject.Parse((string)r.invoice_blob);
            var paymentBlob = JObject.Parse((string)r.payment_blob);


            var data = JObject.Parse(paymentBlob.SelectToken("$.cryptoPaymentData")?.Value<string>()!);
            var conf = data.SelectToken("$.confirmationCount")?.Value<int>();
            values.Add(conf is int o ? o > 0 : null);
            values.Add(data.SelectToken("$.address")?.Value<string>());

            if (data.SelectToken("$.value")?.Value<long>() is long v)
            {
                values.Add(LightMoney.MilliSatoshis(v).ToDecimal(LightMoneyUnit.BTC));
            }
            else if (data.SelectToken("$.amount")?.Value<long>() is long amount)
            {
                values.Add(Money.Satoshis(amount).ToDecimal(MoneyUnit.BTC));
            }
            else
            {
                values.Add(null);
            }
            values.Add(paymentBlob.SelectToken("$.networkFee", false)?.Value<decimal>());
            values.Add(invoiceBlob.SelectToken("$.cryptoData.BTC_LNURLPAY.paymentMethod.ConsumedLightningAddress", false)?.Value<string>());
        }
    }
}
