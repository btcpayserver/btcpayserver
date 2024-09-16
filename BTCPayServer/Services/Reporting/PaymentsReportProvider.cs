using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using Dapper;
using Microsoft.EntityFrameworkCore;
using static BTCPayServer.Client.Models.InvoicePaymentMethodDataModel;

namespace BTCPayServer.Services.Reporting;

public class PaymentsReportProvider : ReportProvider
{
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly PaymentMethodHandlerDictionary _handlers;

    public PaymentsReportProvider(
        ApplicationDbContextFactory dbContextFactory,
        DisplayFormatter displayFormatter,
        InvoiceRepository invoiceRepository,
        BTCPayNetworkProvider btcPayNetworkProvider,
        PaymentMethodHandlerDictionary handlers)
    {
        _btcPayNetworkProvider = btcPayNetworkProvider;
        _handlers = handlers;
        DbContextFactory = dbContextFactory;
        DisplayFormatter = displayFormatter;
        InvoiceRepository = invoiceRepository;
    }
    public override string Name => "Payments";
    private ApplicationDbContextFactory DbContextFactory { get; }
    private DisplayFormatter DisplayFormatter { get; }
    public InvoiceRepository InvoiceRepository { get; }

    ViewDefinition CreateViewDefinition()
    {
        return new()
        {
            Fields =
            {
                new ("Date", "datetime"),
                new ("InvoiceId", "invoice_id"),
                new ("OrderId", "string"),
                new ("Category", "string"),
                new ("PaymentMethodId", "string"),
                new ("Confirmed", "boolean"),
                new ("Address", "string"),
                new ("PaymentCurrency", "string"),
                new ("PaymentAmount", "amount"),
                new ("PaymentMethodFee", "decimal"),
                new ("LightningAddress", "string"),
                new ("InvoiceCurrency", "string"),
                new ("InvoiceCurrencyAmount", "amount"),
                new ("Rate", "amount")
            },
            Charts = 
            {
                new ()
                {
                    Name = "Aggregated by payment's currency",
                    Groups = { "PaymentCurrency", "PaymentMethodId" },
                    Totals = { "PaymentCurrency" },
                    HasGrandTotal = false,
                    Aggregates = { "PaymentAmount" }
                },
                new ()
                {
                    Name = "Aggregated by invoice's currency",
                    Groups = { "InvoiceCurrency" },
                    Totals = { "InvoiceCurrency" },
                    HasGrandTotal = false,
                    Aggregates = { "InvoiceCurrencyAmount" }
                },
                new ()
                {
                    Name = "Group by Lightning Address",
                    Filters = { "typeof this.LightningAddress === 'string' && this.PaymentCurrency == \"BTC\"" },
                    Groups = { "LightningAddress", "InvoiceCurrency" },
                    Aggregates = { "InvoiceCurrencyAmount" },
                    HasGrandTotal = true
                },
                new ()
                {
                    Name = "Group by Lightning Address (Crypto)",
                    Filters = { "typeof this.LightningAddress === 'string' && this.PaymentCurrency == \"BTC\"" },
                    Groups = { "LightningAddress", "PaymentCurrency" },
                    Aggregates = { "PaymentAmount" },
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
        var invoices = await InvoiceRepository.GetInvoices(new InvoiceQuery()
        {
            StoreId = [queryContext.StoreId],
            StartDate = queryContext.From,
            EndDate = queryContext.To,
            OrderByDesc = false,
        }, cancellation);
        
        foreach (var invoice in invoices)
        {
            foreach (var payment in invoice.GetPayments(true))
            {
                var values = queryContext.CreateData();
                values.Add(invoice.InvoiceTime);
                values.Add(invoice.Id);
                values.Add(invoice.Metadata.OrderId);
                var paymentMethodId = payment.PaymentMethodId;
                _handlers.TryGetValue(paymentMethodId, out var handler);
                if (handler is ILightningPaymentHandler)
                {
                    values.Add("Lightning");
                }
                else if (handler is BitcoinLikePaymentHandler)
                    values.Add("On-Chain");
                else
                    values.Add(paymentMethodId.ToString());
                values.Add(paymentMethodId.ToString());

                values.Add(payment.Status is PaymentStatus.Settled);
                values.Add(payment.Destination);
                values.Add(payment.Currency);
                values.Add(new FormattedAmount(payment.Value, payment.Divisibility).ToJObject());
                values.Add(payment.PaymentMethodFee);

                var prompt = invoice.GetPaymentPrompt(PaymentTypes.LNURL.GetPaymentMethodId("BTC"));
                var consumerdLightningAddress = prompt is null || handler is not LNURLPayPaymentHandler lnurlHandler ? null : lnurlHandler.ParsePaymentPromptDetails(prompt.Details).ConsumedLightningAddress;
                values.Add(consumerdLightningAddress);
                values.Add(invoice.Currency);
                if (invoice.TryGetRate(payment.Currency, out var rate))
                {
                    values.Add(DisplayFormatter.ToFormattedAmount(rate * payment.Value, invoice.Currency)); // Currency amount
                    values.Add(DisplayFormatter.ToFormattedAmount(rate, invoice.Currency));
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
}
