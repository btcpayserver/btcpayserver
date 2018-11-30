using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Invoices.Export
{
    public class InvoiceExport
    {
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
            var serializer = new CsvSerializer<ExportInvoiceHolder>();
            var csv = serializer.Serialize(invoices);

            return csv;
        }

        private IEnumerable<ExportInvoiceHolder> convertFromDb(InvoiceEntity invoice)
        {
            var exportList = new List<ExportInvoiceHolder>();
            // in this first version we are only exporting invoices that were paid
            foreach (var payment in invoice.GetPayments())
            {
                // not accounted payments are payments which got double spent like RBfed
                if (!payment.Accounted)
                    continue;

                var cryptoCode = payment.GetPaymentMethodId().CryptoCode;
                var pdata = payment.GetCryptoPaymentData();

                var pmethod = invoice.GetPaymentMethod(payment.GetPaymentMethodId(), null);
                var accounting = pmethod.Calculate();
                var details = pmethod.GetPaymentMethodDetails();

                var target = new ExportInvoiceHolder
                {
                    ReceivedDate = payment.ReceivedTime.UtcDateTime,
                    PaymentId = pdata.GetPaymentId(),
                    CryptoCode = cryptoCode,
                    ConversionRate = pmethod.Rate,
                    PaymentType = details.GetPaymentType() == Payments.PaymentTypes.BTCLike ? "OnChain" : "OffChain",
                    Destination = details.GetPaymentDestination(),
                    PaymentDue = $"{accounting.MinimumTotalDue} {cryptoCode}",
                    PaymentPaid = $"{accounting.CryptoPaid} {cryptoCode}",
                    PaymentOverpaid = $"{accounting.OverpaidHelper} {cryptoCode}",

                    OrderId = invoice.OrderId,
                    StoreId = invoice.StoreId,
                    InvoiceId = invoice.Id,
                    CreatedDate = invoice.InvoiceTime.UtcDateTime,
                    ExpirationDate = invoice.ExpirationTime.UtcDateTime,
                    MonitoringDate = invoice.MonitoringExpiration.UtcDateTime,
                    Status = invoice.Status,
                    ItemCode = invoice.ProductInformation?.ItemCode,
                    ItemDesc = invoice.ProductInformation?.ItemDesc,
                    FiatPrice = invoice.ProductInformation?.Price ?? 0,
                    FiatCurrency = invoice.ProductInformation?.Currency,
                };

                exportList.Add(target);
            }

            exportList = exportList.OrderBy(a => a.ReceivedDate).ToList();

            return exportList;
        }
    }

    public class ExportInvoiceHolder
    {
        public DateTime ReceivedDate { get; set; }
        public string StoreId { get; set; }
        public string OrderId { get; set; }
        public string InvoiceId { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ExpirationDate { get; set; }
        public DateTime MonitoringDate { get; set; }

        public string PaymentId { get; set; }
        public string CryptoCode { get; set; }
        public string Destination { get; set; }
        public string PaymentType { get; set; }
        public string PaymentDue { get; set; }
        public string PaymentPaid { get; set; }
        public string PaymentOverpaid { get; set; }
        public decimal ConversionRate { get; set; }

        public decimal FiatPrice { get; set; }
        public string FiatCurrency { get; set; }
        public string ItemCode { get; set; }
        public string ItemDesc { get; set; }
        public string Status { get; set; }
    }
}
