using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;
using NBitpayClient;

namespace BTCPayServer.Authentication
{
    public class BitTokenEntity
    {
        public string Facade
        {
            get; set;
        }
        public string Value
        {
            get; set;
        }
        public string StoreId
        {
            get; set;
        }
        public string Label
        {
            get; set;
        }
        public DateTimeOffset PairingTime
        {
            get; set;
        }
        public string SIN
        {
            get;
            set;
        }

        public BitTokenEntity Clone(Facade facade)
        {
            return new BitTokenEntity()
            {
                Label = Label,
                Facade = Facade,
                StoreId = StoreId,
                PairingTime = PairingTime,
                SIN = SIN,
                Value = Value
            };
        }
    }
}
