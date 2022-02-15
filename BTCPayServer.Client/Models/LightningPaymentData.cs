using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Lightning;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class LightningPaymentData
    {
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney TotalAmount { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney FeeAmount { get; set; }
    }
}
