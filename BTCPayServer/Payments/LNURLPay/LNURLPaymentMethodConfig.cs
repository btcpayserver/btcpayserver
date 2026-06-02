#nullable enable
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Lightning
{
    public class LNURLPaymentMethodConfig
    {

        public bool UseBech32Scheme { get; set; }

        [JsonProperty("lud12Enabled")]
        public bool LUD12Enabled { get; set; } = true;

        /// <summary>
        /// LUD-21: LNURL-pay verify endpoint. Allows external services to verify
        /// Lightning payment settlement without authentication.
        /// </summary>
        [JsonProperty("lud21Enabled")]
        public bool LUD21Enabled { get; set; } = true;

    }
}
