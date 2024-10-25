using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;

namespace BTCPayServer.Services.Reporting;

public class LegacyInvoiceExportReportProvider : ReportProvider
{
    private readonly CurrencyNameTable _currencyNameTable;
    private readonly InvoiceRepository _invoiceRepository;


    public override string Name { get; } = "Legacy Invoice Export";

    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        var invoices = await _invoiceRepository.GetInvoices(new InvoiceQuery()
        {
            EndDate = queryContext.To,
            StartDate = queryContext.From,
            StoreId = new[] {queryContext.StoreId},
        }, cancellation);

        queryContext.ViewDefinition = new ViewDefinition()
        {
            Fields = new List<StoreReportResponse.Field>()
            {
                new("ReceivedDate", "datetime"),
                new("StoreId", "text"),
                new("OrderId", "text"),
                new("InvoiceId", "invoice_id"),
                new("InvoiceCreatedDate", "datetime"),
                new("InvoiceExpirationDate", "datetime"),
                new("InvoiceMonitoringDate", "datetime"),
                new("PaymentId", "text"),
                new("Destination", "text"),
                new("PaymentType", "text"),
                new("CryptoCode", "text"),
                new("Paid", "text"),
                new("NetworkFee", "text"),
                new("ConversionRate", "number"),
                new("PaidCurrency", "text"),
                new("InvoiceCurrency", "text"),
                new("InvoiceDue", "number"),
                new("InvoicePrice", "number"),
                new("InvoiceItemCode", "text"),
                new("InvoiceItemDesc", "text"),
                new("InvoiceFullStatus", "text"),
                new("InvoiceStatus", "text"),
                new("InvoiceExceptionStatus", "text"),
                new("BuyerEmail", "text"),
                new("Accounted", "boolean")
            }
        };

        foreach (var invoiceEntity in invoices)
        {
            var currency = _currencyNameTable.GetNumberFormatInfo(invoiceEntity.Currency, true);
            var invoiceDue = invoiceEntity.Price;
            var payments = invoiceEntity.GetPayments(false);

            if (payments.Count > 0)
            {
                foreach (var payment in payments)
                {
                    invoiceDue -= payment.InvoicePaidAmount.Net;
                    var data = queryContext.AddData();

                    // Add each field in the order defined in ViewDefinition
                    data.Add(payment.ReceivedTime);
                    data.Add(invoiceEntity.StoreId);
                    data.Add(invoiceEntity.Metadata.OrderId ?? string.Empty);
                    data.Add(invoiceEntity.Id);
                    data.Add(invoiceEntity.InvoiceTime);
                    data.Add(invoiceEntity.ExpirationTime);
                    data.Add(invoiceEntity.MonitoringExpiration);
                    data.Add(payment.Id);
                    data.Add(payment.Destination);
                    data.Add(payment.PaymentMethodId.ToString());
                    data.Add(payment.Currency);
                    data.Add(payment.PaidAmount.Gross.ToString(CultureInfo.InvariantCulture));
                    data.Add(payment.PaymentMethodFee.ToString(CultureInfo.InvariantCulture));
                    data.Add(payment.Rate);
                    data.Add(Math.Round(payment.InvoicePaidAmount.Gross, currency.NumberDecimalDigits)
                        .ToString(CultureInfo.InvariantCulture));
                    data.Add(invoiceEntity.Currency);
                    data.Add(Math.Round(invoiceDue, currency.NumberDecimalDigits));
                    data.Add(invoiceEntity.Price);
                    data.Add(invoiceEntity.Metadata.ItemCode);
                    data.Add(invoiceEntity.Metadata.ItemDesc);
                    data.Add(invoiceEntity.GetInvoiceState().ToString());
#pragma warning disable CS0618 // Type or member is obsolete
                    data.Add(invoiceEntity.Status.ToLegacyStatusString());
                    data.Add(invoiceEntity.ExceptionStatus.ToLegacyExceptionStatusString());
#pragma warning restore CS0618 // Type or member is obsolete
                    data.Add(invoiceEntity.Metadata.BuyerEmail);
                    data.Add(payment.Accounted);
                }
            }
            else
            {
                var data = queryContext.AddData();

                // Add fields for invoices without payments
                data.Add(null); // ReceivedDate
                data.Add(invoiceEntity.StoreId);
                data.Add(invoiceEntity.Metadata.OrderId ?? string.Empty);
                data.Add(invoiceEntity.Id);
                data.Add(invoiceEntity.InvoiceTime);
                data.Add(invoiceEntity.ExpirationTime);
                data.Add(invoiceEntity.MonitoringExpiration);
                data.Add(null); // PaymentId
                data.Add(null); // Destination
                data.Add(null); // PaymentType
                data.Add(null); // CryptoCode
                data.Add(null); // Paid
                data.Add(null); // NetworkFee
                data.Add(null); // ConversionRate
                data.Add(null); // PaidCurrency
                data.Add(invoiceEntity.Currency);
                data.Add(Math.Round(invoiceDue, currency.NumberDecimalDigits)); // InvoiceDue
                data.Add(invoiceEntity.Price);
                data.Add(invoiceEntity.Metadata.ItemCode);
                data.Add(invoiceEntity.Metadata.ItemDesc);
                data.Add(invoiceEntity.GetInvoiceState().ToString());
                data.Add(invoiceEntity.Status.ToLegacyStatusString());
                data.Add(invoiceEntity.ExceptionStatus.ToLegacyExceptionStatusString());
                data.Add(invoiceEntity.Metadata.BuyerEmail);
                data.Add(null); // Accounted
            }
        }
    }

    public LegacyInvoiceExportReportProvider(CurrencyNameTable currencyNameTable, InvoiceRepository invoiceRepository)
    {
        _currencyNameTable = currencyNameTable;
        _invoiceRepository = invoiceRepository;
    }
}
