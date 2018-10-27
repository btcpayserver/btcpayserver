using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using NBitcoin;

namespace BTCPayServer.Data
{
    public class AddressInvoiceData
    {
        /// <summary>
        /// Some crypto currencies share same address prefix
        /// For not having exceptions thrown by two address on different network, we suffix by "#CRYPTOCODE" 
        /// </summary>
        [Obsolete("Use GetHash instead")]
        public string Address
        {
            get; set;
        }


#pragma warning disable CS0618
        public string GetAddress()
        {
            if (Address == null)
                return null;
            var index = Address.LastIndexOf("#", StringComparison.InvariantCulture);
            if (index == -1)
                return Address;
            return Address.Substring(0, index);
        }
        public AddressInvoiceData Set(string address, PaymentMethodId paymentMethodId)
        {
            Address = address + "#" + paymentMethodId.ToString();
            return this;
        }
        public PaymentMethodId GetpaymentMethodId()
        {
            if (Address == null)
                return null;
            var index = Address.LastIndexOf("#", StringComparison.InvariantCulture);
            // Legacy AddressInvoiceData does not have the paymentMethodId attached to the Address
            if (index == -1)
                return PaymentMethodId.Parse("BTC");
            /////////////////////////
            return PaymentMethodId.Parse(Address.Substring(index + 1));
        }
#pragma warning restore CS0618

        public InvoiceData InvoiceData
        {
            get; set;
        }

        public string InvoiceDataId
        {
            get; set;
        }

        public DateTimeOffset? CreatedTime
        {
            get; set;
        }

    }
}
