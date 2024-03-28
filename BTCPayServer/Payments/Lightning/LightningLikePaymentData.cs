using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.JsonConverters;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningLikePaymentData : CryptoPaymentData
    {

        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        public uint256 PaymentHash { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        public uint256 Preimage { get; set; }

        public string GetPaymentProof()
        {
            return Preimage?.ToString();
        }
    }
}
