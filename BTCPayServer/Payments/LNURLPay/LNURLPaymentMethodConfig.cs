#nullable enable
using System;
using BTCPayServer.Lightning;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Lightning
{
    public class LNURLPaymentMethodConfig
    {

        public bool UseBech32Scheme { get; set; }

        [JsonProperty("lud12Enabled")]
        public bool LUD12Enabled { get; set; } = true;

    }
}
