using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Rating;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto.Signers;

namespace BTCPayServer.Services.Reporting
{
    public class ItemsReportProvider : ReportProvider
    {
        public ItemsReportProvider(InvoiceRepository invoiceRepository, CurrencyNameTable currencyNameTable)
        {
            InvoiceRepository = invoiceRepository;
            CurrencyNameTable = currencyNameTable;
        }

        public InvoiceRepository InvoiceRepository { get; }
        public CurrencyNameTable CurrencyNameTable { get; }

        public override string Name => "Items sold";

        public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
        {
            queryContext.ViewDefinition = CreateDefinition();
            foreach (var i in (await InvoiceRepository.GetInvoices(new InvoiceQuery()
            {
                IncludeArchived = true,
                IncludeAddresses = false,
                IncludeEvents = false,
                IncludeRefunds = false,
                StartDate = queryContext.From,
                EndDate = queryContext.To,
                StoreId = new[] { queryContext.StoreId }
            })).OrderBy(c => c.InvoiceTime))
            {
                var values = queryContext.CreateData();
                values.Add(i.InvoiceTime);
                values.Add(i.Id);
                var status = i.Status.ToModernStatus();
                if (status == Client.Models.InvoiceStatus.Expired && i.ExceptionStatus == Client.Models.InvoiceExceptionStatus.None)
                    continue;
                values.Add(status.ToString());
                var appId = AppService.GetAppInternalTags(i)?.FirstOrDefault();
                if (appId is null)
                    continue;
                values.Add(appId);
                if (i.Metadata?.ItemCode is string code)
                {
                    values.Add(code);
                    values.Add(1);
                    values.Add(i.Currency);
                    values.Add(i.Price);
                    queryContext.Data.Add(values);
                }
                else
                {
                    var posData = i.Metadata.PosData?.ToObject<PosAppData>();
                    if (posData.Cart is { } cart)
                    {
                        foreach (var item in cart)
                        {
                            var copy = values.ToList();
                            copy.Add(item.Id);
                            copy.Add(item.Count);
                            copy.Add(i.Currency);
                            copy.Add(item.Price * item.Count);
                            queryContext.Data.Add(copy);
                        }
                    }
                }
            }
            // Round the currency amount
            foreach (var r in queryContext.Data)
            {
                r[^1] = ((decimal)r[^1]).RoundToSignificant(CurrencyNameTable.GetCurrencyData((string)r[^2] ?? "USD", true).Divisibility);
            }
        }

        private ViewDefinition CreateDefinition()
        {
            return new ViewDefinition()
            {
                Fields =
                {
                    new ("Date", "datetime"),
                    new ("InvoiceId", "invoice_id"),
                    new ("State", "string"),
                    new ("AppId", "string"),
                    new ("Item", "string"),
                    new ("Quantity", "decimal"),
                    new ("Currency", "string"),
                    new ("CurrencyAmount", "decimal")
                },
                Charts =
                {
                    new ()
                    {
                        Name = "Summary by items",
                        Groups = { "AppId", "State", "Item" },
                        Aggregates = { "Quantity", "CurrencyAmount" },
                        Totals = { "State" }
                    }
                }
            };
        }
    }
}
