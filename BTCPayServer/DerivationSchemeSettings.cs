using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payments;
using NBitcoin;
using NBXplorer.Client;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;

namespace BTCPayServer
{
    public class DerivationSchemeSettings : ISupportedPaymentMethod
    {
        public static DerivationSchemeSettings Parse(string derivationStrategy, BTCPayNetwork network)
        {
            ArgumentNullException.ThrowIfNull(network);
            ArgumentNullException.ThrowIfNull(derivationStrategy);
            var result = new DerivationSchemeSettings { Network = network };
            var parser = network.GetDerivationSchemeParser();
            if (parser.TryParseXpub(derivationStrategy, ref result) ||
                parser.TryParseXpub(derivationStrategy, ref result, electrum: true))
            {
                return result;
            }

            throw new FormatException($"Invalid Derivation Scheme");
        }

        public static bool TryParseFromJson(string config, BTCPayNetwork network, out DerivationSchemeSettings strategy)
        {
            ArgumentNullException.ThrowIfNull(network);
            ArgumentNullException.ThrowIfNull(config);
            strategy = null;
            try
            {
                strategy = network.NBXplorerNetwork.Serializer.ToObject<DerivationSchemeSettings>(config);
                strategy.Network = network;
            }
            catch { }
            return strategy != null;
        }

        public string GetNBXWalletId()
        {
            return AccountDerivation is null ? null : DBUtils.nbxv1_get_wallet_id(Network.CryptoCode, AccountDerivation.ToString());
        }

        public DerivationSchemeSettings()
        {

        }

        public DerivationSchemeSettings(DerivationStrategyBase derivationStrategy, BTCPayNetwork network)
        {
            ArgumentNullException.ThrowIfNull(network);
            ArgumentNullException.ThrowIfNull(derivationStrategy);
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

        public bool IsHotWallet { get; set; }

        [Obsolete("Use GetSigningAccountKeySettings().AccountKeyPath instead")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public KeyPath AccountKeyPath { get; set; }

        public DerivationStrategyBase AccountDerivation { get; set; }
        public string AccountOriginal { get; set; }

        [Obsolete("Use GetSigningAccountKeySettings().RootFingerprint instead")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public HDFingerprint? RootFingerprint { get; set; }

        [Obsolete("Use GetSigningAccountKeySettings().AccountKey instead")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public BitcoinExtPubKey ExplicitAccountKey { get; set; }

        [JsonIgnore]
        [Obsolete("Use GetSigningAccountKeySettings().AccountKey instead")]
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
