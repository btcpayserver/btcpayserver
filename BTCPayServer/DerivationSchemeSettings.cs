using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BTCPayServer.Payments;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer.Client;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer
{
    public class DerivationSchemeSettings : ISupportedPaymentMethod
    {
        public static DerivationSchemeSettings Parse(string derivationStrategy, BTCPayNetwork network)
        {
            string error = null;
            ArgumentNullException.ThrowIfNull(network);
            ArgumentNullException.ThrowIfNull(derivationStrategy);
            var result = new DerivationSchemeSettings();
            result.Network = network;
            var parser = new DerivationSchemeParser(network);
            if (TryParseXpub(derivationStrategy, parser, ref result, ref error, false) || TryParseXpub(derivationStrategy, parser, ref result, ref error, true))
            {
                return result;
            }

            throw new FormatException($"Invalid Derivation Scheme: {error}");
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

        private static bool TryParseXpub(string xpub, DerivationSchemeParser derivationSchemeParser, ref DerivationSchemeSettings derivationSchemeSettings, ref string error, bool electrum = true)
        {
            if (!electrum)
            {
                var isOD = Regex.Match(xpub, @"\(.*?\)").Success;
                try
                {
                    var result = derivationSchemeParser.ParseOutputDescriptor(xpub);
                    derivationSchemeSettings.AccountOriginal = xpub.Trim();
                    derivationSchemeSettings.AccountDerivation = result.Item1;
                    derivationSchemeSettings.AccountKeySettings = result.Item2.Select((path, i) => new AccountKeySettings()
                    {
                        RootFingerprint = path?.MasterFingerprint,
                        AccountKeyPath = path?.KeyPath,
                        AccountKey = result.Item1.GetExtPubKeys().ElementAt(i).GetWif(derivationSchemeParser.Network)
                    }).ToArray();
                    return true;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    if (isOD)
                    {
                        return false;
                    } // otherwise continue and try to parse input as xpub
                }
            }
            try
            {
                // Extract fingerprint and account key path from export formats that contain them.
                // Possible formats: [fingerprint/account_key_path]xpub, [fingerprint]xpub, xpub
                HDFingerprint? rootFingerprint = null;
                KeyPath accountKeyPath = null;
                var derivationRegex = new Regex(@"^(?:\[(\w+)(?:\/(.*?))?\])?(\w+)$", RegexOptions.IgnoreCase);
                var match = derivationRegex.Match(xpub.Trim());
                if (match.Success)
                {
                    if (!string.IsNullOrEmpty(match.Groups[1].Value))
                        rootFingerprint = HDFingerprint.Parse(match.Groups[1].Value);
                    if (!string.IsNullOrEmpty(match.Groups[2].Value))
                        accountKeyPath = KeyPath.Parse(match.Groups[2].Value);
                    if (!string.IsNullOrEmpty(match.Groups[3].Value))
                        xpub = match.Groups[3].Value;
                }
                derivationSchemeSettings.AccountOriginal = xpub.Trim();
                derivationSchemeSettings.AccountDerivation = electrum ? derivationSchemeParser.ParseElectrum(derivationSchemeSettings.AccountOriginal) : derivationSchemeParser.Parse(derivationSchemeSettings.AccountOriginal);
                derivationSchemeSettings.AccountKeySettings = derivationSchemeSettings.AccountDerivation.GetExtPubKeys()
                    .Select(key => new AccountKeySettings
                    {
                        AccountKey = key.GetWif(derivationSchemeParser.Network)
                    }).ToArray();
                if (derivationSchemeSettings.AccountDerivation is DirectDerivationStrategy direct && !direct.Segwit)
                    derivationSchemeSettings.AccountOriginal = null; // Saving this would be confusing for user, as xpub of electrum is legacy derivation, but for btcpay, it is segwit derivation
                // apply initial matches if there were no results from parsing
                if (rootFingerprint != null && derivationSchemeSettings.AccountKeySettings[0].RootFingerprint == null)
                {
                    derivationSchemeSettings.AccountKeySettings[0].RootFingerprint = rootFingerprint;
                }
                if (accountKeyPath != null && derivationSchemeSettings.AccountKeySettings[0].AccountKeyPath == null)
                {
                    derivationSchemeSettings.AccountKeySettings[0].AccountKeyPath = accountKeyPath;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static bool TryParseBSMSFile(string filecontent, DerivationSchemeParser derivationSchemeParser, ref DerivationSchemeSettings derivationSchemeSettings,
            out string error)
        {
            error = null;
            try
            {
                string[] lines = filecontent.Split(
                    new[] {"\r\n", "\r", "\n"},
                    StringSplitOptions.None
                );

                if (!lines[0].Trim().Equals("BSMS 1.0"))
                {;
                    return false;
                }

                var descriptor = lines[1];
                var derivationPath = lines[2].Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?? "/0/*";
                if (derivationPath == "No path restrictions")
                {
                    derivationPath = "/0/*";
                }
                if(derivationPath != "/0/*")
                {
                    error = "BTCPay Server can only derive address to the deposit and change paths";
                    return false;
                }
                
                
                descriptor = descriptor.Replace("/**", derivationPath);
                var testAddress = BitcoinAddress.Create( lines[3], derivationSchemeParser.Network);
               var result =  derivationSchemeParser.ParseOutputDescriptor(descriptor);
               
               var deposit = new NBXplorer.KeyPathTemplates(null).GetKeyPathTemplate(DerivationFeature.Deposit);
               var line = result.Item1.GetLineFor(deposit).Derive(0);
               
               if (testAddress.ScriptPubKey != line.ScriptPubKey)
                {
                    error = "BSMS test address did not match our generated address";
                    return false;
                }

                derivationSchemeSettings.Source = "BSMS";
                derivationSchemeSettings.AccountDerivation = result.Item1;
                derivationSchemeSettings.AccountOriginal = descriptor.Trim();
                derivationSchemeSettings.AccountKeySettings = result.Item2.Select((path, i) => new AccountKeySettings()
                {
                    RootFingerprint = path?.MasterFingerprint,
                    AccountKeyPath = path?.KeyPath,
                    AccountKey = result.Item1.GetExtPubKeys().ElementAt(i).GetWif(derivationSchemeParser.Network)
                }).ToArray();
                return true;
            }
            catch (Exception e)
            {
                error = $"BSMS parse error: {e.Message}";
                return false;
            }
        }
        public static bool TryParseFromWalletFile(string fileContents, BTCPayNetwork network, out DerivationSchemeSettings settings, out string error)
        {
            settings = null;
            error = null;
            ArgumentNullException.ThrowIfNull(fileContents);
            ArgumentNullException.ThrowIfNull(network);
            var result = new DerivationSchemeSettings();
            var derivationSchemeParser = new DerivationSchemeParser(network);
            JObject jobj;
            try
            {
                if (HexEncoder.IsWellFormed(fileContents))
                {
                    fileContents = Encoding.UTF8.GetString(Encoders.Hex.DecodeData(fileContents));
                }
                jobj = JObject.Parse(fileContents);
            }
            catch
            {
                if (TryParseBSMSFile(fileContents, derivationSchemeParser,ref result, out var bsmsError))
                {
                    settings = result;
                    settings.Network = network;
                    return true;
                }
                if (bsmsError is not null)
                {
                    error = bsmsError;
                    return false;
                }
                result.Source = "GenericFile";
                if (TryParseXpub(fileContents, derivationSchemeParser, ref result, ref error) ||
                    TryParseXpub(fileContents, derivationSchemeParser, ref result, ref error, false))
                {
                    settings = result;
                    settings.Network = network;
                    return true;
                }

                return false;
            }

            // Electrum
            if (jobj.ContainsKey("keystore"))
            {
                result.Source = "ElectrumFile";
                jobj = (JObject)jobj["keystore"];

                if (!jobj.ContainsKey("xpub") ||
                    !TryParseXpub(jobj["xpub"].Value<string>(), derivationSchemeParser, ref result, ref error))
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
            }
            // Specter
            else if (jobj.ContainsKey("descriptor") && jobj.ContainsKey("blockheight"))
            {
                result.Source = "SpecterFile";

                if (!TryParseXpub(jobj["descriptor"].Value<string>(), derivationSchemeParser, ref result, ref error, false))
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
            }
            // Wasabi
            else
            {
                result.Source = "WasabiFile";
                if (!jobj.ContainsKey("ExtPubKey") ||
                    !TryParseXpub(jobj["ExtPubKey"].Value<string>(), derivationSchemeParser, ref result, ref error, false))
                {
                    return false;
                }
                if (jobj.ContainsKey("MasterFingerprint"))
                {
                    try
                    {
                        var mfpString = jobj["MasterFingerprint"].ToString().Trim();
                        // https://github.com/zkSNACKs/WalletWasabi/pull/1663#issuecomment-508073066

                        if (uint.TryParse(mfpString, out var fingerprint))
                        {
                            result.AccountKeySettings[0].RootFingerprint = new HDFingerprint(fingerprint);
                        }
                        else
                        {
                            var shouldReverseMfp = jobj.ContainsKey("ColdCardFirmwareVersion") &&
                                                   jobj["ColdCardFirmwareVersion"].ToString() == "2.1.0";
                            var bytes = Encoders.Hex.DecodeData(mfpString);
                            result.AccountKeySettings[0].RootFingerprint = shouldReverseMfp ? new HDFingerprint(bytes.Reverse().ToArray()) : new HDFingerprint(bytes);
                        }
                    }

                    catch { return false; }
                }
                if (jobj.ContainsKey("AccountKeyPath"))
                {
                    try
                    {
                        result.AccountKeySettings[0].AccountKeyPath = new KeyPath(jobj["AccountKeyPath"].Value<string>());
                    }
                    catch { return false; }
                }
                if (jobj.ContainsKey("DerivationPath"))
                {
                    try
                    {
                        result.AccountKeySettings[0].AccountKeyPath = new KeyPath(jobj["DerivationPath"].Value<string>().ToLowerInvariant());
                    }
                    catch { return false; }
                }

                if (jobj.ContainsKey("ColdCardFirmwareVersion"))
                {
                    result.Source = "ColdCard";
                }

                if (jobj.ContainsKey("CoboVaultFirmwareVersion"))
                {
                    result.Source = "CoboVault";
                }
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
