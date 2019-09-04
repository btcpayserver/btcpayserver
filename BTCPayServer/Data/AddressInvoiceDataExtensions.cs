using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;

namespace BTCPayServer.Data
{
    public static class AddressInvoiceDataExtensions
    {
#pragma warning disable CS0618
        public static string GetAddress(this AddressInvoiceData addressInvoiceData)
        {
            if (addressInvoiceData.Address == null)
                return null;
            var index = addressInvoiceData.Address.LastIndexOf("#", StringComparison.InvariantCulture);
            if (index == -1)
                return addressInvoiceData.Address;
            return addressInvoiceData.Address.Substring(0, index);
        }
        public static AddressInvoiceData Set(this AddressInvoiceData addressInvoiceData, string address, PaymentMethodId paymentMethodId)
        {
            addressInvoiceData.Address = address + "#" + paymentMethodId.ToString();
            return addressInvoiceData;
        }
        public static PaymentMethodId GetpaymentMethodId(this AddressInvoiceData addressInvoiceData)
        {
            if (addressInvoiceData.Address == null)
                return null;
            var index = addressInvoiceData.Address.LastIndexOf("#", StringComparison.InvariantCulture);
            // Legacy AddressInvoiceData does not have the paymentMethodId attached to the Address
            if (index == -1)
                return PaymentMethodId.Parse("BTC");
            /////////////////////////
            return PaymentMethodId.Parse(addressInvoiceData.Address.Substring(index + 1));
        }
#pragma warning restore CS0618
    }
}
