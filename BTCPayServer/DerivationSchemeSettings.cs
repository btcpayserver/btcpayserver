using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer
{
    public class DerivationSchemeSettings : ISupportedPaymentMethod
    {
        public static DerivationSchemeSettings Parse(string derivationStrategy, BTCPayNetwork network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (derivationStrategy == null)
                throw new ArgumentNullException(nameof(derivationStrategy));
            var result = new NBXplorer.DerivationStrategy.DerivationStrategyFactory(network.NBitcoinNetwork).Parse(derivationStrategy);
            return new DerivationSchemeSettings(result, network) { AccountOriginal = derivationStrategy.Trim() };
        }

        public static bool TryParseFromColdcard(string coldcardExport, BTCPayNetwork network, out DerivationSchemeSettings settings)
        {
            settings = null;
            if (coldcardExport == null)
                throw new ArgumentNullException(nameof(coldcardExport));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            var result = new DerivationSchemeSettings();
            result.Source = "Coldcard";
            var derivationSchemeParser = new DerivationSchemeParser(network.NBitcoinNetwork);
            JObject jobj = null;
            try
            {
                jobj = JObject.Parse(coldcardExport);
                jobj = (JObject)jobj["keystore"];
            }
            catch
            {
                return false;
            }

            if (jobj.ContainsKey("xpub"))
            {
                try
                {
                    result.AccountOriginal = jobj["xpub"].Value<string>().Trim();
                    result.AccountDerivation = derivationSchemeParser.ParseElectrum(result.AccountOriginal);
                    if (result.AccountDerivation is DirectDerivationStrategy direct && !direct.Segwit)
                        result.AccountOriginal = null; // Saving this would be confusing for user, as xpub of electrum is legacy derivation, but for btcpay, it is segwit derivation
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            if (jobj.ContainsKey("label"))
            {
                try
                {
                    result.Label = jobj["label"].Value<string>();
                }
                catch { return false; }
            }

            if (jobj.ContainsKey("ckcc_xfp"))
            {
                try
                {
                    result.RootFingerprint = new HDFingerprint(jobj["ckcc_xfp"].Value<uint>());
                }
                catch { return false; }
            }

            if (jobj.ContainsKey("derivation"))
            {
                try
                {
                    result.AccountKeyPath = new KeyPath(jobj["derivation"].Value<string>());
                }
                catch { return false; }
            }
            else
            {
                result.AccountKeyPath = new KeyPath();
            }
            settings = result;
            settings.Network = network;
            return true;
        }

        public DerivationSchemeSettings()
        {

        }
        public DerivationSchemeSettings(DerivationStrategyBase derivationStrategy, BTCPayNetwork network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (derivationStrategy == null)
                throw new ArgumentNullException(nameof(derivationStrategy));
            AccountDerivation = derivationStrategy;
            Network = network;
        }
        [JsonIgnore]
        public BTCPayNetwork Network { get; set; }
        public string Source { get; set; }
        public KeyPath AccountKeyPath { get; set; }
        
        public DerivationStrategyBase AccountDerivation { get; set; }
        public string AccountOriginal { get; set; }

        public HDFingerprint? RootFingerprint { get; set; }

        public string Label { get; set; }

        [JsonIgnore]
        public PaymentMethodId PaymentId => new PaymentMethodId(Network.CryptoCode, PaymentTypes.BTCLike);

        public override string ToString()
        {
            return AccountDerivation.ToString();
        }
        public string ToPrettyString()
        {
            return string.IsNullOrEmpty(Label) ? Label :
                   String.IsNullOrEmpty(AccountOriginal) ? AccountOriginal :
                   ToString();
        }
    }
}
