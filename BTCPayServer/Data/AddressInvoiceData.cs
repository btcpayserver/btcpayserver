using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        public ScriptId GetHash()
        {
            if (Address == null)
                return null;
            var index = Address.IndexOf("#");
            if (index == -1)
                return new ScriptId(Address);
            return new ScriptId(Address.Substring(0, index));
        }
        public AddressInvoiceData SetHash(ScriptId scriptId, string cryptoCode)
        {
            Address = scriptId + "#" + cryptoCode;
            return this;
        }
        public string GetCryptoCode()
        {
            if (Address == null)
                return null;
            var index = Address.IndexOf("#");
            if (index == -1)
                return "BTC";
            return Address.Substring(index + 1);
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
