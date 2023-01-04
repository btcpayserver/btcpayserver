using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BTCPayServer.Services.Rates;
using CsvHelper.Configuration;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Invoices.Export
{
    public class InvoiceExport
    {
        public BTCPayNetworkProvider Networks { get; }
        public CurrencyNameTable Currencies { get; }

        public InvoiceExport(CurrencyNameTable currencies)
        {
            Currencies = currencies;
        }
        public string Process(InvoiceEntity[] invoices, string fileFormat)
        {
            var csvInvoices = new List<ExportInvoiceHolder>();
            foreach (var i in invoices)
            {
                csvInvoices.AddRange(convertFromDb(i));
            }

            if (String.Equals(fileFormat, "json", StringComparison.OrdinalIgnoreCase))
                return processJson(csvInvoices);
            else if (String.Equals(fileFormat, "csv", StringComparison.OrdinalIgnoreCase))
                return processCsv(csvInvoices);
            else
                throw new Exception("Export format not supported");
        }

        private string processJson(List<ExportInvoiceHolder> invoices)
        {
            var serializerSett = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
            var json = JsonConvert.SerializeObject(invoices, Formatting.Indented, serializerSett);

            return json;
        }

        private string processCsv(List<ExportInvoiceHolder> invoices)
        {
            using StringWriter writer = new StringWriter();
            using var csvWriter = new CsvHelper.CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture), true);
            csvWriter.WriteHeader<ExportInvoiceHolder>();
            csvWriter.NextRecord();
            csvWriter.WriteRecords(invoices);
            csvWriter.Flush();
            return writer.ToString();
        }

        private IEnumerable<ExportInvoiceHolder> convertFromDb(InvoiceEntity invoice)
        {
            var exportList = new List<ExportInvoiceHolder>();
            var currency = Currencies.GetNumberFormatInfo(invoice.Currency, true);
            var invoiceDue = invoice.Price;
            var payments = invoice.GetPayments(false);
            // Get invoices with payments
            if (payments.Count > 0)
            {
                foreach (var payment in payments)
                {
                    var cryptoCode = payment.GetPaymentMethodId().CryptoCode;
                    var pdata = payment.GetCryptoPaymentData();

                    var pmethod = invoice.GetPaymentMethod(payment.GetPaymentMethodId());
                    var paidAfterNetworkFees = pdata.GetValue() - payment.NetworkFee;
                    invoiceDue -= paidAfterNetworkFees * pmethod.Rate;

                    var target = new ExportInvoiceHolder
                    {
                        ReceivedDate = payment.ReceivedTime.UtcDateTime,
                        PaymentId = pdata.GetPaymentId(),
                        CryptoCode = cryptoCode,
                        ConversionRate = pmethod.Rate,
                        PaymentType = payment.GetPaymentMethodId().PaymentType.ToPrettyString(),
                        Destination = pdata.GetDestination(),
                        Paid = pdata.GetValue().ToString(CultureInfo.InvariantCulture),
                        PaidCurrency = Math.Round(pdata.GetValue() * pmethod.Rate, currency.NumberDecimalDigits).ToString(CultureInfo.InvariantCulture),
                        // Adding NetworkFee because Paid doesn't take into account network fees
                        // so if fee is 10000 satoshis, customer can essentially send infinite number of tx
                        // and merchant effectivelly would receive 0 BTC, invoice won't be paid
                        // while looking just at export you could sum Paid and assume merchant "received payments"
                        NetworkFee = payment.NetworkFee.ToString(CultureInfo.InvariantCulture),
                        InvoiceDue = Math.Round(invoiceDue, currency.NumberDecimalDigits),
                        OrderId = invoice.Metadata.OrderId ?? string.Empty,
                        StoreId = invoice.StoreId,
                        InvoiceId = invoice.Id,
                        InvoiceCreatedDate = invoice.InvoiceTime.UtcDateTime,
                        InvoiceExpirationDate = invoice.ExpirationTime.UtcDateTime,
                        InvoiceMonitoringDate = invoice.MonitoringExpiration.UtcDateTime,
#pragma warning disable CS0618 // Type or member is obsolete
                        InvoiceFullStatus = invoice.GetInvoiceState().ToString(),
                        InvoiceStatus = invoice.StatusString,
                        InvoiceExceptionStatus = invoice.ExceptionStatusString,
#pragma warning restore CS0618 // Type or member is obsolete
                        InvoiceItemCode = invoice.Metadata.ItemCode,
                        InvoiceItemDesc = invoice.Metadata.ItemDesc,
                        InvoicePrice = invoice.Price,
                        InvoiceCurrency = invoice.Currency,
                        BuyerEmail = invoice.Metadata.BuyerEmail,
                        Accounted = payment.Accounted
                    };

                    exportList.Add(target);
                }
            }
            else
            {
                var target = new ExportInvoiceHolder
                {
                    InvoiceDue = Math.Round(invoiceDue, currency.NumberDecimalDigits),
                    OrderId = invoice.Metadata.OrderId ?? string.Empty,
                    StoreId = invoice.StoreId,
                    InvoiceId = invoice.Id,
                    InvoiceCreatedDate = invoice.InvoiceTime.UtcDateTime,
                    InvoiceExpirationDate = invoice.ExpirationTime.UtcDateTime,
                    InvoiceMonitoringDate = invoice.MonitoringExpiration.UtcDateTime,
#pragma warning disable CS0618 // Type or member is obsolete
                    InvoiceFullStatus = invoice.GetInvoiceState().ToString(),
                    InvoiceStatus = invoice.StatusString,
                    InvoiceExceptionStatus = invoice.ExceptionStatusString,
#pragma warning restore CS0618 // Type or member is obsolete
                    InvoiceItemCode = invoice.Metadata.ItemCode,
                    InvoiceItemDesc = invoice.Metadata.ItemDesc,
                    InvoicePrice = invoice.Price,
                    InvoiceCurrency = invoice.Currency,
                    BuyerEmail = invoice.Metadata.BuyerEmail
                };

                exportList.Add(target);
            }

            exportList = exportList.OrderBy(a => a.ReceivedDate).ToList();

            return exportList;
        }
    }

    public class ExportInvoiceHolder
    {
        public DateTime? ReceivedDate { get; set; }
        public string StoreId { get; set; }
        public string OrderId { get; set; }
        public string InvoiceId { get; set; }
        public DateTime InvoiceCreatedDate { get; set; }
        public DateTime InvoiceExpirationDate { get; set; }
        public DateTime InvoiceMonitoringDate { get; set; }

        public string PaymentId { get; set; }
        public string Destination { get; set; }
        public string PaymentType { get; set; }
        public string CryptoCode { get; set; }
        public string Paid { get; set; }
        public string NetworkFee { get; set; }
        public decimal ConversionRate { get; set; }
        public string PaidCurrency { get; set; }
        public string InvoiceCurrency { get; set; }
        public decimal InvoiceDue { get; set; }
        public decimal InvoicePrice { get; set; }
        public string InvoiceItemCode { get; set; }
        public string InvoiceItemDesc { get; set; }
        public string InvoiceFullStatus { get; set; }
        public string InvoiceStatus { get; set; }
        public string InvoiceExceptionStatus { get; set; }
        public string BuyerEmail { get; set; }
        public bool Accounted { get; set; }
    }
}
