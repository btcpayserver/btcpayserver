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
    public class DerivationSchemeSettings
    {
        public static DerivationSchemeSettings Parse(string derivationStrategy, BTCPayNetwork network)
        {
            ArgumentNullException.ThrowIfNull(network);
            ArgumentNullException.ThrowIfNull(derivationStrategy);
            var result = new DerivationSchemeSettings();
            var parser = network.GetDerivationSchemeParser();
            if (parser.TryParseXpub(derivationStrategy, ref result) ||
                parser.TryParseXpub(derivationStrategy, ref result, electrum: true))
            {
                return result;
            }

            throw new FormatException($"Invalid Derivation Scheme");
        }

        public string GetNBXWalletId(Network network)
        {
            return AccountDerivation is null ? null : DBUtils.nbxv1_get_wallet_id(network.NetworkSet.CryptoCode, AccountDerivation.ToString());
        }

        public DerivationSchemeSettings()
        {
        }

        public DerivationSchemeSettings(DerivationStrategyBase derivationStrategy, BTCPayNetwork network)
        {
            ArgumentNullException.ThrowIfNull(network);
            ArgumentNullException.ThrowIfNull(derivationStrategy);
            AccountDerivation = derivationStrategy;
            AccountKeySettings = derivationStrategy.GetExtPubKeys().Select(c => new AccountKeySettings()
            {
                AccountKey = c.GetWif(network.NBitcoinNetwork)
            }).ToArray();
        }


        [JsonIgnore]
        [Obsolete("Use AccountKeySettings[0].AccountKey instead")]
        public BitcoinExtPubKey SigningKey
        {
            get
            {
                // There should always be at least one account key
                return AccountKeySettings[0].AccountKey;
            }
            set
            {
                // Ignored, this is legacy stuff that should disappear
            }
        }

        public string Source { get; set; }

        public bool IsHotWallet { get; set; }

        public DerivationStrategyBase AccountDerivation { get; set; }
        public string AccountOriginal { get; set; }

#nullable enable
        [Obsolete("Use GetFirstAccountKeySettings isntead")]
        public AccountKeySettings GetSigningAccountKeySettings()
            // There should always be at least one account key
        => (AccountKeySettings ?? []).First();

        public AccountKeySettings GetFirstAccountKeySettings()
            // There should always be at least one account key
            => (AccountKeySettings ?? []).First();

        public AccountKeySettings? GetAccountKeySettingsFromRoot(IHDKey rootKey)
            => GetAccountKeySettingsFromRoot(rootKey.GetPublicKey().GetHDFingerPrint());

        public AccountKeySettings? GetAccountKeySettingsFromRoot(HDFingerprint rootFingerprint)
            => (AccountKeySettings ?? []).FirstOrDefault(a => a.RootFingerprint == rootFingerprint);

        [Obsolete("Use GetAccountKeySettingsFromRoot instead")]
        public AccountKeySettings? GetSigningAccountKeySettings(IHDKey rootKey)
        => GetAccountKeySettingsFromRoot(rootKey.GetPublicKey().GetHDFingerPrint());

        [Obsolete("Use GetAccountKeySettingsFromRoot instead")]
        public AccountKeySettings? GetSigningAccountKeySettings(HDFingerprint rootFingerprint)
            => GetAccountKeySettingsFromRoot(rootFingerprint);

        [Obsolete("Use AccountKeySettings[0] instead")]
        // There should always be at least one account key
        public AccountKeySettings? GetSigningAccountKeySettingsOrDefault() => this.AccountKeySettings[0];
#nullable restore
        public AccountKeySettings[] AccountKeySettings { get; set; }

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

        #region MultiSig related settings
        public bool IsMultiSigOnServer { get; set; }

        // some hardware devices like Jade require sending full input transactions if there are multiple inputs
        // https://github.com/Blockstream/Jade/blob/0d6ce77bf23ef2b5dc43cdae3967b4207e8cad52/main/process/sign_tx.c#L586
        public bool DefaultIncludeNonWitnessUtxo { get; set; }
        #endregion

        public override string ToString()
        {
            return AccountDerivation.ToString();
        }
        public string ToPrettyString()
        {
            return !string.IsNullOrEmpty(Label) ? Label :
                   !string.IsNullOrEmpty(AccountOriginal) ? AccountOriginal :
                   ToString();
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
