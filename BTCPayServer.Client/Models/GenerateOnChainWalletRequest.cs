using System.Collections.Generic;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Client.Models;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client
{
    public class GenerateOnChainWalletRequest
    {
        public string Label { get; set; }
        public int AccountNumber { get; set; } = 0;
        [JsonConverter(typeof(MnemonicJsonConverter))]
        public Mnemonic ExistingMnemonic { get; set; }
        [JsonConverter(typeof(WordlistJsonConverter))]
        public NBitcoin.Wordlist WordList { get; set; }

        [JsonConverter(typeof(WordcountJsonConverter))]
        public NBitcoin.WordCount? WordCount { get; set; } = NBitcoin.WordCount.Twelve;

        [JsonConverter(typeof(StringEnumConverter))]
        public NBitcoin.ScriptPubKeyType ScriptPubKeyType { get; set; } = ScriptPubKeyType.Segwit;
        public string Passphrase { get; set; }
        public bool ImportKeysToRPC { get; set; }
        public bool SavePrivateKeys { get; set; }
    }
    public class GenerateOnChainWalletResponse : GenericPaymentMethodData
    {
        public class ConfigData
        {
            public string Label { get; set; }
            public string AccountDerivation { get; set; }
            [JsonExtensionData]
            IDictionary<string, JToken> AdditionalData { get; set; }
        }
        [JsonConverter(typeof(MnemonicJsonConverter))]
        public Mnemonic Mnemonic { get; set; }
        public new ConfigData Config { get; set; }
    }
}
