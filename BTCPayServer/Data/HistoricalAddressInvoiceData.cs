using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public class HistoricalAddressInvoiceData
    {
        public string InvoiceDataId
        {
            get; set;
        }

        public InvoiceData InvoiceData
        {
            get; set;
        }

        /// <summary>
        /// Some crypto currencies share same address prefix
        /// For not having exceptions thrown by two address on different network, we suffix by "#CRYPTOCODE" 
        /// </summary>
        [Obsolete("Use GetCryptoCode instead")]
        public string Address
        {
            get; set;
        }


        [Obsolete("Use GetCryptoCode instead")]
        public string CryptoCode { get; set; }

#pragma warning disable CS0618
        public Payments.PaymentMethodId GetPaymentMethodId()
        {
            return string.IsNullOrEmpty(CryptoCode) ? new Payments.PaymentMethodId("BTC", Payments.PaymentTypes.BTCLike)
                                                    : Payments.PaymentMethodId.Parse(CryptoCode);
        }
        public string GetAddress()
        {
            if (Address == null)
                return null;
            var index = Address.IndexOf("#", StringComparison.InvariantCulture);
            if (index == -1)
                return Address;
            return Address.Substring(0, index);
        }
        public HistoricalAddressInvoiceData SetAddress(string depositAddress, string cryptoCode)
        {
            Address = depositAddress + "#" + cryptoCode;
            CryptoCode = cryptoCode;
            return this;
        }
#pragma warning restore CS0618

        public DateTimeOffset Assigned
        {
            get; set;
        }

        public DateTimeOffset? UnAssigned
        {
            get; set;
        }
    }
}
