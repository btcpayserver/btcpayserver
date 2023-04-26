#nullable enable
using System;
using BTCPayServer.Lightning;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Lightning
{
    public class LNURLPaySupportedPaymentMethod : ISupportedPaymentMethod
    {
        public string CryptoCode { get; set; } = string.Empty;

        [JsonIgnore]
        public PaymentMethodId PaymentId => new PaymentMethodId(CryptoCode, PaymentTypes.LNURLPay);

        public bool UseBech32Scheme { get; set; }

        public bool LUD12Enabled { get; set; } = true;

    }
}
