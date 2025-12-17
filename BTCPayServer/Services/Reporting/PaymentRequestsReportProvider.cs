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
                new StoreReportResponse.Field("Status", "string"),
                new StoreReportResponse.Field("RequestedAmount", "amount"),
                new StoreReportResponse.Field("RequestedCurrency", "string"),


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

        var paymentRequests = await ctx.PaymentRequests
            .AsNoTracking()
            .Where(p => p.StoreDataId == queryContext.StoreId)
            .Where(p => p.Created >= queryContext.From && p.Created <= queryContext.To)
            .OrderBy(p => p.Created)
            .ToListAsync(cancellation);

        if (paymentRequests.Count == 0)
            return;

        var network = networkProvider.DefaultNetwork;
        var walletId = new WalletId(queryContext.StoreId, network.CryptoCode);

        var paymentRequestIds = paymentRequests.Select(pr => pr.Id).ToArray();

        var orderIds = paymentRequests
            .Select(pr => PaymentRequestRepository.GetOrderIdForPaymentRequest(pr.Id))
            .Distinct()
            .ToArray();

        var labelsTask = walletRepository.GetWalletLabelsForObjects(
            walletId,
            WalletObjectData.Types.PaymentRequest,
            paymentRequestIds
        );

        var invoicesTask = invoiceRepository.GetInvoices(new InvoiceQuery
        {
            StoreId = new[] { queryContext.StoreId },
            StartDate = queryContext.From,
            EndDate = queryContext.To,
            OrderId = orderIds
        }, cancellation);

        await Task.WhenAll(labelsTask, invoicesTask);

        var labelsByPaymentRequestId = labelsTask.Result;
        var invoices = invoicesTask.Result;

        var invoicesByOrderId = invoices
            .GroupBy(i => i.Metadata.OrderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var paymentRequest in paymentRequests)
        {
            var prBlob = paymentRequest.GetBlob();
            var prOrderId = PaymentRequestRepository.GetOrderIdForPaymentRequest(paymentRequest.Id);

            labelsByPaymentRequestId.TryGetValue(paymentRequest.Id, out var labelTuples);
            var labelsString = labelTuples is { Length: > 0 }
                ? string.Join(", ", labelTuples.Select(l => l.Label))
                : "";

            if (!invoicesByOrderId.TryGetValue(prOrderId, out var prInvoices) || prInvoices.Count == 0)
            {
                var row = queryContext.CreateData();
                row.Add(paymentRequest.Created);
                row.Add(paymentRequest.ReferenceId);
                row.Add(paymentRequest.Title);
                row.Add(labelsString);
                row.Add(paymentRequest.Status.ToString());
                row.Add(displayFormatter.ToFormattedAmount(paymentRequest.Amount, paymentRequest.Currency));
                row.Add(paymentRequest.Currency);

                row.Add(null);
                row.Add(prOrderId);

                row.Add("No Payment");
                row.Add("None");

                row.Add("None");
                row.Add(displayFormatter.ToFormattedAmount(0m, paymentRequest.Currency));

                row.Add(paymentRequest.Currency);
                row.Add(displayFormatter.ToFormattedAmount(0m, paymentRequest.Currency));
                row.Add(displayFormatter.ToFormattedAmount(0m, paymentRequest.Currency));

                queryContext.Data.Add(row);
                continue;
            }

            foreach (var invoice in prInvoices)
            {
                foreach (var payment in invoice.GetPayments(true))
                {
                    var row = queryContext.CreateData();

                    row.Add(payment.ReceivedTime);
                    row.Add(paymentRequest.ReferenceId);
                    row.Add(paymentRequest.Title);
                    row.Add(labelsString);
                    row.Add(paymentRequest.Status.ToString());
                    row.Add(displayFormatter.ToFormattedAmount(paymentRequest.Amount, paymentRequest.Currency));
                    row.Add(paymentRequest.Currency);

                    row.Add(invoice.Id);
                    row.Add(invoice.Metadata.OrderId);

                    var paymentMethodId = payment.PaymentMethodId;
                    handlers.TryGetValue(paymentMethodId, out var handler);

                    var paymentMethodCategory = handler switch
                        {
                            ILightningPaymentHandler => "Lightning",
                            BitcoinLikePaymentHandler => "On-Chain",
                            _ => paymentMethodId.ToString()
                        };

                    row.Add(paymentMethodCategory);
                    row.Add(paymentMethodId.ToString());

                    row.Add(payment.Currency);
                    row.Add(displayFormatter.ToFormattedAmount(payment.Value, payment.Currency));

                    row.Add(invoice.Currency);

                    if (invoice.TryGetRate(payment.Currency, out var rate))
                    {
                        row.Add(displayFormatter.ToFormattedAmount(rate * payment.Value, invoice.Currency));
                        row.Add(displayFormatter.ToFormattedAmount(rate, invoice.Currency));
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
