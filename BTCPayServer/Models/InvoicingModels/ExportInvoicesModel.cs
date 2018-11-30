using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;

namespace BTCPayServer.Models.InvoicingModels
{
    public class ExportInvoicesModel
    {
        public string Process(InvoiceEntity[] invoices, string fileFormat)
        {
            if (String.Equals(fileFormat, "json", StringComparison.OrdinalIgnoreCase))
                return processJson(invoices);
            else
                throw new Exception("Export format not supported");
        }

        private string processJson(InvoiceEntity[] invoices)
        {
            var csvInvoices = new List<ExportInvoiceHolder>();
            foreach (var i in invoices)
            {
                csvInvoices.AddRange(convertFromDb(i));
            }

            var serializerSett = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
            var json = JsonConvert.SerializeObject(csvInvoices, Formatting.Indented, serializerSett);

            return json;
        }

        private IEnumerable<ExportInvoiceHolder> convertFromDb(InvoiceEntity invoice)
        {
            var exportList = new List<ExportInvoiceHolder>();
            // in this first version we are only exporting invoices that were paid
            foreach (var payment in invoice.GetPayments())
            {
                var cryptoCode = payment.GetPaymentMethodId().CryptoCode;
                var pdata = payment.GetCryptoPaymentData();

                var pmethod = invoice.GetPaymentMethod(payment.GetPaymentMethodId(), null);
                var accounting = pmethod.Calculate();
                var onchainDetails = pmethod.GetPaymentMethodDetails() as BitcoinLikeOnChainPaymentMethod;

                var target = new ExportInvoiceHolder
                {
                    PaymentId = pdata.GetPaymentId(),
                    CryptoCode = cryptoCode,
                    ConversionRate = pmethod.Rate,
                    Address = onchainDetails?.DepositAddress,
                    PaymentDue = $"{accounting.MinimumTotalDue} {cryptoCode}",
                    PaymentPaid = $"{accounting.CryptoPaid} {cryptoCode}",
                    PaymentOverpaid = $"{accounting.OverpaidHelper} {cryptoCode}",

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

            return exportList;
        }
    }

    public class ExportInvoiceHolder
    {
        public string PaymentId { get; set; }
        public string CryptoCode { get; set; }
        public decimal ConversionRate { get; set; }
        public string Address { get; set; }
        public string PaymentDue { get; set; }
        public string PaymentPaid { get; set; }
        public string PaymentOverpaid { get; set; }

        public string InvoiceId { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ExpirationDate { get; set; }
        public DateTime MonitoringDate { get; set; }
        public string Status { get; set; }
        public string ItemCode { get; set; }
        public string ItemDesc { get; set; }
        public decimal FiatPrice { get; set; }
        public string FiatCurrency { get; set; }
    }
}
