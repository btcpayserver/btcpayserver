using BTCPayServer.Client.JsonConverters;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client
{
    public class GenerateOnChainWalletRequest
    {
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
}
