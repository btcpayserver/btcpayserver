using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BTCPayServer.Services.Rates;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Services.Invoices.Export
{
    public class InvoiceExport
    {
        public BTCPayNetworkProvider Networks { get; }
        public CurrencyNameTable Currencies { get; }

        private readonly PullPaymentHostedService _pullPaymentHostedService;
        private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;

        public InvoiceExport(CurrencyNameTable currencies, 
            PullPaymentHostedService pullPaymentHostedService,
            BTCPayNetworkJsonSerializerSettings jsonSerializerSettings)
        {
            Currencies = currencies;
            _pullPaymentHostedService = pullPaymentHostedService;
            _jsonSerializerSettings = jsonSerializerSettings;
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

            // we are only exporting invoices that were paid
            //  - payments
            foreach (var payment in invoice.GetPayments(true))
            {
                var exportItem = this.GetInvoiceBase(invoice);

                var cryptoCode = payment.GetPaymentMethodId().CryptoCode;
                var pdata = payment.GetCryptoPaymentData();
                var pmethod = invoice.GetPaymentMethod(payment.GetPaymentMethodId());
                var paidAfterNetworkFees = pdata.GetValue() - payment.NetworkFee;
                invoiceDue -= paidAfterNetworkFees * pmethod.Rate;

                exportItem.ReceivedDate = payment.ReceivedTime.UtcDateTime;
                exportItem.PaymentId = pdata.GetPaymentId();
                exportItem.CryptoCode = cryptoCode;
                exportItem.ConversionRate = pmethod.Rate;
                exportItem.PaymentType = payment.GetPaymentMethodId().PaymentType.ToPrettyString();
                exportItem.Destination = pdata.GetDestination();
                exportItem.Paid = pdata.GetValue().ToString(CultureInfo.InvariantCulture);
                exportItem.PaidCurrency = Math.Round(pdata.GetValue() * pmethod.Rate, currency.NumberDecimalDigits).ToString(CultureInfo.InvariantCulture);
                // Adding NetworkFee because Paid doesn't take into account network fees
                // so if fee is 10000 satoshis, customer can essentially send infinite number of tx
                // and merchant effectivelly would receive 0 BTC, invoice won't be paid
                // while looking just at export you could sum Paid and assume merchant "received payments"
                exportItem.NetworkFee = payment.NetworkFee.ToString(CultureInfo.InvariantCulture);

                exportList.Add(exportItem);
            }

            //  - pull payments
            foreach (var refund in invoice.Refunds)
            {
                var pp = _pullPaymentHostedService.GetPullPayment(refund.PullPaymentDataId, true).Result;
                var blob = pp.GetBlob();
                
                // Approved and sent only
                foreach (var payout in pp.Payouts.Where(p => p.State == PayoutState.Completed))
                {
                    var exportItem = this.GetInvoiceBase(invoice);

                    var payoutBlob = payout.GetBlob(_jsonSerializerSettings);
                    var pm = invoice.GetPaymentMethod(payout.GetPaymentMethodId());

                    exportItem.ReceivedDate = payout.Date.UtcDateTime;
                    exportItem.PaymentId = payout.Id;
                    exportItem.CryptoCode = payout.GetPaymentMethodId().CryptoCode;
                    exportItem.ConversionRate = pm.Rate;
                    exportItem.PaymentType = payout.GetPaymentMethodId().PaymentType.ToPrettyString();
                    exportItem.Destination = payout.Destination;
                    exportItem.Paid = (-1 * payoutBlob.CryptoAmount).ToString();
                    exportItem.PaidCurrency = Math.Round(-1 * payoutBlob.CryptoAmount.GetValueOrDefault() * pm.Rate, currency.NumberDecimalDigits).ToString(CultureInfo.InvariantCulture);
#pragma warning disable CS0618 // Type or member is obsolete
                    exportItem.NetworkFee = pm.NextNetworkFee.ToString();
#pragma warning restore CS0618 // Type or member is obsolete

                    exportList.Add(exportItem);
                }
            }

            exportList = exportList.OrderBy(a => a.ReceivedDate).ToList();

            return exportList;
        }

        private ExportInvoiceHolder GetInvoiceBase(InvoiceEntity invoice)
        {
            var currency = Currencies.GetNumberFormatInfo(invoice.Currency, true);
            var invoiceDue = invoice.Price;

            // Create base export item
            return new ExportInvoiceHolder
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
                BuyerEmail = invoice.Metadata.BuyerEmail,
            };
        }
    }

    public class ExportInvoiceHolder
    {
        public DateTime ReceivedDate { get; set; }
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
    }
}
