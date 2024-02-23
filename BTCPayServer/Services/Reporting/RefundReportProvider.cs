using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services.Reporting;

public class RefundReportProvider : ReportProvider
{
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly CurrencyNameTable _currencyNameTable;
    private readonly InvoiceRepository _invoiceRepository;
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    public override string Name { get; } = "Refunds";


    public RefundReportProvider(
        PullPaymentHostedService pullPaymentHostedService,
        ApplicationDbContextFactory applicationDbContextFactory,  
        CurrencyNameTable currencyNameTable, 
        InvoiceRepository invoiceRepository, 
        BTCPayNetworkProvider btcPayNetworkProvider)
    {
        _pullPaymentHostedService = pullPaymentHostedService;
        _applicationDbContextFactory = applicationDbContextFactory;
        _currencyNameTable = currencyNameTable;
        _invoiceRepository = invoiceRepository;
        _btcPayNetworkProvider = btcPayNetworkProvider;
    }
    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        await using var context = _applicationDbContextFactory.CreateContext();

        var refunds = await context.Refunds
            .Include(data => data.InvoiceData)
            .Include(data => data.PullPaymentData)
            .Where(data =>
                data.PullPaymentData.StoreId == queryContext.StoreId &&
                data.PullPaymentData.StartDate >= queryContext.From &&
                data.PullPaymentData.StartDate <= queryContext.To).ToListAsync(cancellationToken: cancellation);
        
        
        queryContext.ViewDefinition = new ViewDefinition()
        {
            Fields = new List<StoreReportResponse.Field>()
            {
                new("RefundInitiationDate", "datetime"),
                new("InvoiceCreatedDate", "datetime"),
                new("PullPaymentId", "string"),
                new("InvoiceId", "invoice_id"),
                new("InvoiceAmount", "amount"),
                new("InvoiceCurrency", "string"),
                new("RefundAmount", "amount"),
                new("RefundCurrency", "string"),
                new("RefundPaymentMethods", "string"),
                new("RefundClaimedAmount", "amount")
            }
        };
        

        foreach (var refund in refunds)
        {
            var pullPaymentBlob = refund.PullPaymentData.GetBlob();
            var invoice = refund.InvoiceData.GetBlob(_btcPayNetworkProvider);
            // var currency = _currencyNameTable.GetNumberFormatInfo(invoice.Currency, true);
            // var invoiceDue = invoice.Price;
            // var payments = invoice.GetPayments(false);
            var progress =
                _pullPaymentHostedService.CalculatePullPaymentProgress(refund.PullPaymentData, DateTimeOffset.Now);
            var data = queryContext.AddData();
            data.Add(refund.PullPaymentData.StartDate);
            data.Add(invoice.InvoiceTime);
            data.Add(refund.PullPaymentData.Id);
            data.Add(refund.InvoiceDataId);
            data.Add(invoice.Price);
            data.Add(invoice.Currency);
            data.Add(pullPaymentBlob.Limit);
            data.Add(pullPaymentBlob.Currency);
            data.Add(string.Join(',', pullPaymentBlob.SupportedPaymentMethods.Select(s => s.ToPrettyString())));
            data.Add(progress.Completed + progress.Awaiting);
        }
    }
}
