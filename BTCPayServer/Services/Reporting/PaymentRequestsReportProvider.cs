using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.PaymentRequests;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services.Reporting;

public class PaymentRequestsReportProvider(
    ApplicationDbContextFactory dbContextFactory,
    InvoiceRepository invoiceRepository,
    PaymentMethodHandlerDictionary handlers,
    WalletRepository walletRepository,
    BTCPayNetworkProvider networkProvider,
    DisplayFormatter displayFormatter)
    : ReportProvider
{
    public override string Name => "Requests";

    private ViewDefinition CreateViewDefinition()
    {
        return new ViewDefinition
        {
            Fields =
            {
                new StoreReportResponse.Field("Date", "datetime"),
                new StoreReportResponse.Field("ReferenceId", "string"),
                new StoreReportResponse.Field("Title", "string"),
                new StoreReportResponse.Field("Labels", "string"),

                new StoreReportResponse.Field("InvoiceId", "invoice_id"),
                new StoreReportResponse.Field("OrderId", "string"),

                new StoreReportResponse.Field("PaymentMethod", "string"),
                new StoreReportResponse.Field("PaymentMethodId", "string"),

                new StoreReportResponse.Field("PaymentCurrency", "string"),
                new StoreReportResponse.Field("PaymentAmount", "amount"),

                new StoreReportResponse.Field("InvoiceCurrency", "string"),
                new StoreReportResponse.Field("InvoiceCurrencyAmount", "amount"),
                new StoreReportResponse.Field("Rate", "amount")
            },
            Charts =
            {
                new ChartDefinition
                {
                    Name = "Revenue by label and Payment Method",
                    Groups = { "Labels", "PaymentMethod"},
                    Aggregates = { "InvoiceCurrencyAmount" },
                    Totals = { "Labels" },
                    HasGrandTotal = true
                }
            }
        };
    }

    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        queryContext.ViewDefinition = CreateViewDefinition();

        await using var ctx = dbContextFactory.CreateContext();

        var prs = await ctx.PaymentRequests
            .Where(p => p.StoreDataId == queryContext.StoreId)
            .Where(p => p.Created >= queryContext.From && p.Created <= queryContext.To)
            .OrderBy(p => p.Created)
            .ToListAsync(cancellation);

        if (!prs.Any())
            return;

        var network = networkProvider.DefaultNetwork;
        var walletId = new WalletId(queryContext.StoreId, network.CryptoCode);

        var labelsCache = new Dictionary<string, string>();

        var orderIds = prs
            .Select(pr => PaymentRequestRepository.GetOrderIdForPaymentRequest(pr.Id))
            .Distinct()
            .ToArray();

        var invoices = await invoiceRepository.GetInvoices(new InvoiceQuery
        {
            StoreId = [queryContext.StoreId],
            StartDate = queryContext.From,
            EndDate = queryContext.To,
            OrderId = orderIds
        }, cancellation);

        var invoicesByOrderId = invoices
            .GroupBy(i => i.Metadata.OrderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var pr in prs)
        {
            var prBlob = pr.GetBlob();
            var prOrderId = PaymentRequestRepository.GetOrderIdForPaymentRequest(pr.Id);

            if (!labelsCache.TryGetValue(pr.Id, out var labelsString))
            {
                var objectId = new WalletObjectId(
                    walletId,
                    WalletObjectData.Types.PaymentRequest,
                    pr.Id
                );

                var labelTuples = await walletRepository.GetWalletLabels(objectId);
                labelsString = labelTuples.Any()
                    ? string.Join(", ", labelTuples.Select(l => l.Label))
                    : "None";

                labelsCache[pr.Id] = labelsString;
            }

            if (!invoicesByOrderId.TryGetValue(prOrderId, out var prInvoices) || prInvoices.Count == 0)
            {
                var row = queryContext.CreateData();
                row.Add(pr.Created);                   // Date
                row.Add(pr.ReferenceId);                        // PaymentRequestId
                row.Add(prBlob.Title);                 // Title
                row.Add(labelsString);                 // Labels

                row.Add(null);                         // InvoiceId
                row.Add(prOrderId);                    // OrderId

                row.Add("No Payment");                 // PaymentMethod
                row.Add("None");                       // PaymentMethodId

                row.Add("None");                       // PaymentCurrency
                row.Add(displayFormatter.ToFormattedAmount(0m, pr.Currency));

                row.Add(pr.Currency);                  // InvoiceCurrency
                row.Add(displayFormatter.ToFormattedAmount(0m, pr.Currency)); // InvoiceCurrencyAmount
                row.Add(displayFormatter.ToFormattedAmount(0m, pr.Currency)); // Rate

                queryContext.Data.Add(row);
                continue;
            }

            foreach (var invoice in prInvoices)
            {
                foreach (var payment in invoice.GetPayments(true))
                {
                    var row = queryContext.CreateData();

                    row.Add(payment.ReceivedTime);
                    row.Add(pr.ReferenceId);                    // PaymentRequestId
                    row.Add(prBlob.Title);             // Title
                    row.Add(labelsString);             // Labels

                    row.Add(invoice.Id);               // InvoiceId
                    row.Add(invoice.Metadata.OrderId); // OrderId

                    var paymentMethodId = payment.PaymentMethodId;
                    handlers.TryGetValue(paymentMethodId, out var handler);

                    string paymentMethodCategory;
                    if (handler is ILightningPaymentHandler)
                        paymentMethodCategory = "Lightning";
                    else if (handler is BitcoinLikePaymentHandler)
                        paymentMethodCategory = "On-Chain";
                    else
                        paymentMethodCategory = paymentMethodId.ToString(); // plugins, stablecoins, etc.

                    row.Add(paymentMethodCategory);         // PaymentMethod
                    row.Add(paymentMethodId.ToString());    // PaymentMethodId

                    row.Add(payment.Currency);              // PaymentCurrency
                    row.Add(displayFormatter.ToFormattedAmount(payment.Value, payment.Currency)); // PaymentAmount

                    row.Add(invoice.Currency);              // InvoiceCurrency

                    if (invoice.TryGetRate(payment.Currency, out var rate))
                    {
                        row.Add(displayFormatter.ToFormattedAmount(rate * payment.Value, invoice.Currency)); // InvoiceCurrencyAmount
                        row.Add(displayFormatter.ToFormattedAmount(rate, invoice.Currency));                  // Rate
                    }
                    else
                    {
                        row.Add(displayFormatter.ToFormattedAmount(0m, invoice.Currency));
                        row.Add(displayFormatter.ToFormattedAmount(0m, invoice.Currency));
                    }

                    queryContext.Data.Add(row);
                }
            }
        }
    }
}
