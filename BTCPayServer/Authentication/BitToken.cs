﻿using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;
using NBitpayClient;

namespace BTCPayServer.Authentication
{
    public class BitTokenEntity
    {
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

        public BitTokenEntity Clone()
        {
            return new BitTokenEntity()
            {
                Label = Label,
                StoreId = StoreId,
                PairingTime = PairingTime,
                SIN = SIN,
                Value = Value
            };
        }
    }
}
