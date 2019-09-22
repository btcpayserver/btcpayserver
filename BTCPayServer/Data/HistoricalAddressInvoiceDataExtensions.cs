using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public static class HistoricalAddressInvoiceDataExtensions
    {
#pragma warning disable CS0618
        public static Payments.PaymentMethodId GetPaymentMethodId(this HistoricalAddressInvoiceData historicalAddressInvoiceData)
        {
            return string.IsNullOrEmpty(historicalAddressInvoiceData.CryptoCode) ? new Payments.PaymentMethodId("BTC", Payments.PaymentTypes.BTCLike)
                                                    : Payments.PaymentMethodId.Parse(historicalAddressInvoiceData.CryptoCode);
        }
        public static string GetAddress(this HistoricalAddressInvoiceData historicalAddressInvoiceData)
        {
            if (historicalAddressInvoiceData.Address == null)
                return null;
            var index = historicalAddressInvoiceData.Address.IndexOf("#", StringComparison.InvariantCulture);
            if (index == -1)
                return historicalAddressInvoiceData.Address;
            return historicalAddressInvoiceData.Address.Substring(0, index);
        }
        public static HistoricalAddressInvoiceData SetAddress(this HistoricalAddressInvoiceData historicalAddressInvoiceData, string depositAddress, string cryptoCode)
        {
            historicalAddressInvoiceData.Address = depositAddress + "#" + cryptoCode;
            historicalAddressInvoiceData.CryptoCode = cryptoCode;
            return historicalAddressInvoiceData;
        }
#pragma warning restore CS0618
    }
}
