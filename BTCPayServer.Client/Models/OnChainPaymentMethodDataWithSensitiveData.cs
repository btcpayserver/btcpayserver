using BTCPayServer.Client.JsonConverters;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class OnChainPaymentMethodDataWithSensitiveData : OnChainPaymentMethodData
    {
        public OnChainPaymentMethodDataWithSensitiveData()
        {
        }

        public OnChainPaymentMethodDataWithSensitiveData(string cryptoCode, string derivationScheme, bool enabled,
            string label, RootedKeyPath accountKeyPath, Mnemonic mnemonic, string paymentMethod) : base(cryptoCode, derivationScheme, enabled,
            label, accountKeyPath, paymentMethod)
        {
            Mnemonic = mnemonic;
        }

        [JsonConverter(typeof(MnemonicJsonConverter))]
        public Mnemonic Mnemonic { get; set; }
    }
}
