using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Services.Reporting;

public class ProductsReportProvider : ReportProvider
{
    public ProductsReportProvider(
        InvoiceRepository invoiceRepository,
        DisplayFormatter displayFormatter,
        AppService apps)
    {
        InvoiceRepository = invoiceRepository;
        _displayFormatter = displayFormatter;
        Apps = apps;
    }

    private readonly DisplayFormatter _displayFormatter;
    private InvoiceRepository InvoiceRepository { get; }
    private AppService Apps { get; }

    public override string Name => "Sales";

    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        var appsById = (await Apps.GetApps(queryContext.StoreId)).ToDictionary(o => o.Id);
        var tagAllinvoicesApps = appsById.Values.Where(a => a.TagAllInvoices).ToList();
        queryContext.ViewDefinition = CreateDefinition();
        foreach (var i in (await InvoiceRepository.GetInvoices(new InvoiceQuery
        {
            IncludeArchived = true,
            IncludeAddresses = false,
            IncludeRefunds = false,
            StartDate = queryContext.From,
            EndDate = queryContext.To,
            StoreId = new[] { queryContext.StoreId }
        }, cancellation)).OrderBy(c => c.InvoiceTime))
        {
            var values = queryContext.CreateData();
            values.Add(i.InvoiceTime);
            values.Add(i.Id);
            var status = i.Status;
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
                if (AppService.TryParsePosCartItems(i.Metadata?.PosData, out var items))
                {
                    foreach (var item in items)
                    {
                        var copy = values.ToList();
                        copy.Add(item.Id);
                        copy.Add(item.Count);
                        copy.Add(item.Price * item.Count);
                        copy.Add(i.Currency);
                        queryContext.Data.Add(copy);
                    }
                }
                else if (i.Metadata?.ItemCode is string code)
                {
                    values.Add(code);
                    values.Add(1);
                    values.Add(i.Price);
                    values.Add(i.Currency);
                    queryContext.Data.Add(values);
                }
            }
        }
        // Round the currency amount
        foreach (var r in queryContext.Data)
        {
            var amount = (decimal)r[^2];
            var currency = (string)r[^1] ?? "USD";
            r[^2] = _displayFormatter.ToFormattedAmount(amount, currency);
        }
    }

    private ViewDefinition CreateDefinition()
    {
        return new ViewDefinition
        {
            Fields =
            {
                new ("Date", "datetime"),
                new ("InvoiceId", "invoice_id"),
                new ("State", "string"),
                new ("AppId", "string"),
                new ("Product", "string"),
                new ("Quantity", "integer"),
                new ("CurrencyAmount", "amount"),
                new ("Currency", "string")
            },
            Charts =
            {
                new ()
                {
                    Name = "Summary",
                    Groups = { "AppId", "Currency", "State", "Product" },
                    Aggregates = { "Quantity", "CurrencyAmount" },
                    Totals = { "State" }
                }
            }
        };
    }
}
