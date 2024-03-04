using System.Collections.Generic;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Lightning;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using LNURL;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments
{
    public class LNURLPayPaymentMethodDetails : LigthningPaymentPromptDetails
    {
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney GeneratedBoltAmount { get; set; }
        public bool Bech32Mode { get; set; }

        public string ProvidedComment { get; set; }
        public string ConsumedLightningAddress { get; set; }
        public LNURLPayRequest PayRequest { get; set; }
    }
}
