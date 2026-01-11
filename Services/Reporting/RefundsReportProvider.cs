#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services.Reporting
{
    public class RefundsReportProvider : ReportProvider
    {
        private readonly BTCPayNetworkJsonSerializerSettings _serializerSettings;
        private readonly DisplayFormatter _displayFormatter;

        private ViewDefinition CreateDefinition()
        {
            return new ViewDefinition
            {
                Fields = new List<StoreReportResponse.Field>
                {
                    new("Date", "datetime"),
                    new("InvoiceId", "invoice_id"),
                    new("Currency", "string"),
                    new("Completed", "amount"),
                    new("Awaiting", "amount"),
                    new("Limit", "amount"),
                    new("FullyPaid", "boolean")
                },
                Charts =
                {
                    new ()
                    {
                        Name = "Aggregated amount",
                        Groups = { "Currency" },
                        HasGrandTotal = false,
                        Aggregates = { "Awaiting", "Completed", "Limit" }
                    }
                }
            };
        }
        public override string Name => "Refunds";

        public ApplicationDbContextFactory DbContextFactory { get; }

        public RefundsReportProvider(
            ApplicationDbContextFactory dbContextFactory,
            BTCPayNetworkJsonSerializerSettings serializerSettings,
            DisplayFormatter displayFormatter)
        {
            DbContextFactory = dbContextFactory;
            _serializerSettings = serializerSettings;
            _displayFormatter = displayFormatter;
        }
        record RefundRow(DateTimeOffset Created, string InvoiceId, string PullPaymentId, string Currency, decimal Limit)
        {
            public decimal Completed { get; set; }
            public decimal Awaiting { get; set; }
        }
        public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
        {
            queryContext.ViewDefinition = CreateDefinition();
            RefundRow? currentRow = null;
            await using var ctx = DbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            var rows = await conn.QueryAsync(
            """
            SELECT i."Created", i."Id" AS "InvoiceId", p."State", p."PayoutMethodId", p."Currency" AS "PayoutCurrency", p."OriginalAmount", pp."Id" AS "PullPaymentId", pp."Limit", pp."Currency" AS "PPCurrency" FROM "Invoices" i
            JOIN "Refunds" r ON r."InvoiceDataId"= i."Id"
            JOIN "PullPayments" pp ON r."PullPaymentDataId"=pp."Id"
            LEFT JOIN "Payouts" p ON p."PullPaymentDataId"=pp."Id"
            WHERE i."StoreDataId" = @storeId
            AND i."Created" >= @start AND i."Created" <= @end
            AND pp."Archived" IS FALSE
            ORDER BY i."Created", pp."Id"
            """, new { start = queryContext.From, end = queryContext.To, storeId = queryContext.StoreId });
            foreach (var r in rows)
            {
                if ((string)r.PullPaymentId != currentRow?.PullPaymentId)
                {
                    AddRow(queryContext, currentRow);
                    currentRow = new(r.Created, r.InvoiceId, r.PullPaymentId, r.PPCurrency, r.Limit);
                }
                if (r.OriginalAmount is null)
                    continue;
                decimal originalAmount = (decimal)r.OriginalAmount;
                var state = Enum.Parse<PayoutState>((string)r.State);
                if (state == PayoutState.Cancelled)
                    continue;
                if (state is PayoutState.Completed)
                    currentRow.Completed += originalAmount;
                else
                    currentRow.Awaiting += originalAmount;
            }
            AddRow(queryContext, currentRow);
        }

        private void AddRow(QueryContext queryContext, RefundRow? currentRow)
        {
            if (currentRow is null)
                return;
            var data = queryContext.AddData();
            data.Add(currentRow.Created);
            data.Add(currentRow.InvoiceId);
            data.Add(currentRow.Currency);
            data.Add(_displayFormatter.ToFormattedAmount(currentRow.Completed, currentRow.Currency));
            data.Add(_displayFormatter.ToFormattedAmount(currentRow.Awaiting, currentRow.Currency));
            data.Add(_displayFormatter.ToFormattedAmount(currentRow.Limit, currentRow.Currency));
            data.Add(currentRow.Limit <= currentRow.Completed);
        }
    }
}
