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
            var result = network.NBXplorerNetwork.DerivationStrategyFactory.Parse(derivationStrategy);
            return new DerivationSchemeSettings(result, network) { AccountOriginal = derivationStrategy.Trim() };
        }

        public static bool TryParseFromJson(string config, BTCPayNetwork network, out DerivationSchemeSettings strategy)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            strategy = null;
            try
            {
                strategy = network.NBXplorerNetwork.Serializer.ToObject<DerivationSchemeSettings>(config);
                strategy.Network = network;
            }
            catch { }
            return strategy != null;
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
            var derivationSchemeParser = new DerivationSchemeParser(network);
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
                    result.AccountKeySettings = new AccountKeySettings[1];
                    result.AccountKeySettings[0] = new AccountKeySettings();
                    result.AccountKeySettings[0].AccountKey = result.AccountDerivation.GetExtPubKeys().Single().GetWif(network.NBitcoinNetwork);
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
                    result.AccountKeySettings[0].RootFingerprint = new HDFingerprint(jobj["ckcc_xfp"].Value<uint>());
                }
                catch { return false; }
            }

            if (jobj.ContainsKey("derivation"))
            {
                try
                {
                    result.AccountKeySettings[0].AccountKeyPath = new KeyPath(jobj["derivation"].Value<string>());
                }
                catch { return false; }
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
            AccountKeySettings = derivationStrategy.GetExtPubKeys().Select(c => new AccountKeySettings()
            {
                AccountKey = c.GetWif(network.NBitcoinNetwork)
            }).ToArray();
        }


        BitcoinExtPubKey _SigningKey;
        public BitcoinExtPubKey SigningKey
        {
            get
            {
                return _SigningKey ?? AccountKeySettings?.Select(k => k.AccountKey).FirstOrDefault();
            }
            set
            {
                _SigningKey = value;
            }
        }

        [JsonIgnore]
        public BTCPayNetwork Network { get; set; }
        public string Source { get; set; }

        [Obsolete("Use GetAccountKeySettings().AccountKeyPath instead")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public KeyPath AccountKeyPath { get; set; }

        public DerivationStrategyBase AccountDerivation { get; set; }
        public string AccountOriginal { get; set; }

        [Obsolete("Use GetAccountKeySettings().RootFingerprint instead")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public HDFingerprint? RootFingerprint { get; set; }

        [Obsolete("Use GetAccountKeySettings().AccountKey instead")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public BitcoinExtPubKey ExplicitAccountKey { get; set; }

        [JsonIgnore]
        [Obsolete("Use GetAccountKeySettings().AccountKey instead")]
        public BitcoinExtPubKey AccountKey
        {
            get
            {
                return ExplicitAccountKey ?? new BitcoinExtPubKey(AccountDerivation.GetExtPubKeys().First(), Network.NBitcoinNetwork);
            }
        }

        public AccountKeySettings GetSigningAccountKeySettings()
        {
            return AccountKeySettings.Single(a => a.AccountKey == SigningKey);
        }

        AccountKeySettings[] _AccountKeySettings;
        public AccountKeySettings[] AccountKeySettings
        {
            get
            {
                // Legacy
                if (_AccountKeySettings == null)
                {
                    if (this.Network == null)
                        return null;
                    _AccountKeySettings = AccountDerivation.GetExtPubKeys().Select(e => new AccountKeySettings()
                    {
                        AccountKey = e.GetWif(this.Network.NBitcoinNetwork),
                    }).ToArray();
#pragma warning disable CS0618 // Type or member is obsolete
                    _AccountKeySettings[0].AccountKeyPath = AccountKeyPath;
                    _AccountKeySettings[0].RootFingerprint = RootFingerprint;
                    ExplicitAccountKey = null;
                    AccountKeyPath = null;
                    RootFingerprint = null;
#pragma warning restore CS0618 // Type or member is obsolete
                }
                return _AccountKeySettings;
            }
            set
            {
                _AccountKeySettings = value;
            }
        }

        public IEnumerable<NBXplorer.Models.PSBTRebaseKeyRules> GetPSBTRebaseKeyRules()
        {
            foreach (var accountKey in AccountKeySettings)
            {
                if (accountKey.GetRootedKeyPath() is RootedKeyPath rootedKeyPath)
                {
                    yield return new NBXplorer.Models.PSBTRebaseKeyRules()
                    {
                        AccountKey = accountKey.AccountKey,
                        AccountKeyPath = rootedKeyPath
                    };
                }
            }
        }

        public string Label { get; set; }

        [JsonIgnore]
        public PaymentMethodId PaymentId => new PaymentMethodId(Network.CryptoCode, PaymentTypes.BTCLike);

        public override string ToString()
        {
            return AccountDerivation.ToString();
        }
        public string ToPrettyString()
        {
            return !string.IsNullOrEmpty(Label) ? Label :
                   !String.IsNullOrEmpty(AccountOriginal) ? AccountOriginal :
                   ToString();
        }

        public string ToJson()
        {
            return Network.NBXplorerNetwork.Serializer.ToString(this);
        }

        public void RebaseKeyPaths(PSBT psbt)
        {
            foreach (var rebase in GetPSBTRebaseKeyRules())
            {
                psbt.RebaseKeyPaths(rebase.AccountKey, rebase.AccountKeyPath);
            }
        }
    }
    public class AccountKeySettings
    {
        public HDFingerprint? RootFingerprint { get; set; }
        public KeyPath AccountKeyPath { get; set; }

        public RootedKeyPath GetRootedKeyPath()
        {
            if (RootFingerprint is HDFingerprint fp && AccountKeyPath != null)
                return new RootedKeyPath(fp, AccountKeyPath);
            return null;
        }
        public BitcoinExtPubKey AccountKey { get; set; }
        public bool IsFullySetup()
        {
            return AccountKeyPath != null && RootFingerprint is HDFingerprint;
        }
    }
}
