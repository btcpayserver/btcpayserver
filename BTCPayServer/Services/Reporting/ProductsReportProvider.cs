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

namespace BTCPayServer.Services.Reporting;

public class ProductsReportProvider : ReportProvider
{
    public ProductsReportProvider(InvoiceRepository invoiceRepository, CurrencyNameTable currencyNameTable, AppService apps)
    {
        InvoiceRepository = invoiceRepository;
        CurrencyNameTable = currencyNameTable;
        Apps = apps;
    }

    public InvoiceRepository InvoiceRepository { get; }
    public CurrencyNameTable CurrencyNameTable { get; }
    public AppService Apps { get; }

    public override string Name => "Products sold";

    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        var appsById = (await Apps.GetApps(queryContext.StoreId)).ToDictionary(o => o.Id);
        var tagAllinvoicesApps = appsById.Values.Where(a => a.TagAllInvoices).ToList();
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
        }, cancellation)).OrderBy(c => c.InvoiceTime))
        {
            var values = queryContext.CreateData();
            values.Add(i.InvoiceTime);
            values.Add(i.Id);
            var status = i.Status.ToModernStatus();
            if (status == Client.Models.InvoiceStatus.Expired && i.ExceptionStatus == Client.Models.InvoiceExceptionStatus.None)
                continue;
            values.Add(status.ToString());

            // There are two ways an invoice belong to a particular app.
            // 1. The invoice is internally tagged with the app id
            // 2. The app is a tag all invoices app
            // In both cases, we want to include the invoice in the report
            var appIds = tagAllinvoicesApps.Select(a => a.Id);
            var taggedAppId = AppService.GetAppInternalTags(i)?.FirstOrDefault();
            if (taggedAppId is string)
                appIds = appIds.Concat(new[] { taggedAppId }).Distinct().ToArray();

            foreach (var appId in appIds)
            {
                values = values.ToList();
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
                    if (AppService.TryParsePosCartItems(i.Metadata?.PosData, out var items))
                    {
                        foreach (var item in items)
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
                new ("Product", "string"),
                new ("Quantity", "decimal"),
                new ("Currency", "string"),
                new ("CurrencyAmount", "decimal")
            },
            Charts =
            {
                new ()
                {
                    Name = "Summary by products",
                    Groups = { "AppId", "Currency", "State", "Product" },
                    Aggregates = { "Quantity", "CurrencyAmount" },
                    Totals = { "State" }
                }
            }
        };
    }
}
