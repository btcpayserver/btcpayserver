using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTCPayServer.Common;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;

namespace BTCPayServer
{
    public enum DerivationType
    {
        Legacy,
        SegwitP2SH,
        Segwit
    }
    public class BTCPayDefaultSettings
    {
        static BTCPayDefaultSettings()
        {
            _Settings = new Dictionary<ChainName, BTCPayDefaultSettings>();
        }

        static readonly Dictionary<ChainName, BTCPayDefaultSettings> _Settings;

        public static BTCPayDefaultSettings GetDefaultSettings(ChainName chainType)
        {
            if (_Settings.TryGetValue(chainType, out var v))
                return v;
            lock (_Settings)
            {
                if (_Settings.TryGetValue(chainType, out v))
                    return v;
                var settings = new BTCPayDefaultSettings();
                _Settings.Add(chainType, settings);
                settings.DefaultDataDirectory = StandardConfiguration.DefaultDataDirectory.GetDirectory("BTCPayServer", NBXplorerDefaultSettings.GetFolderName(chainType));
                settings.DefaultPluginDirectory =
                    StandardConfiguration.DefaultDataDirectory.GetDirectory("BTCPayServer", "Plugins");
                settings.DefaultConfigurationFile = Path.Combine(settings.DefaultDataDirectory, "settings.config");
                settings.DefaultPort = (chainType == ChainName.Mainnet ? 23000 :
                                                      chainType == ChainName.Regtest ? 23002
                                                                                     : 23001);
            }
            return _Settings[chainType];
        }

        public string DefaultDataDirectory { get; set; }
        public string DefaultPluginDirectory { get; set; }
        public string DefaultConfigurationFile { get; set; }
        public int DefaultPort { get; set; }
    }

    public class BTCPayNetwork : BTCPayNetworkBase
    {
        public Network NBitcoinNetwork { get { return NBXplorerNetwork?.NBitcoinNetwork; } }
        public NBXplorer.NBXplorerNetwork NBXplorerNetwork { get; set; }
        public bool SupportRBF { get; set; }
        public string LightningImagePath { get; set; }
        public BTCPayDefaultSettings DefaultSettings { get; set; }
        public KeyPath CoinType { get; set; }

        public Dictionary<uint, DerivationType> ElectrumMapping = new Dictionary<uint, DerivationType>();

        public virtual bool WalletSupported { get; set; } = true;
        public virtual bool ReadonlyWallet { get; set; } = false;
        public virtual bool VaultSupported { get; set; } = false;
        public int MaxTrackedConfirmation { get; set; } = 6;
        public bool SupportPayJoin { get; set; } = false;
        public bool SupportLightning { get; set; } = true;

        public override T ToObject<T>(string json)
        {
            return NBXplorerNetwork.Serializer.ToObject<T>(json);
        }

        public override string ToString<T>(T obj)
        {
            return NBXplorerNetwork.Serializer.ToString(obj);
        }
        public virtual IEnumerable<(MatchedOutput matchedOutput, OutPoint outPoint)> GetValidOutputs(NewTransactionEvent evtOutputs)
        {
            return evtOutputs.Outputs.Select(output =>
            {
                var outpoint = new OutPoint(evtOutputs.TransactionData.TransactionHash, output.Index);
                return (output, outpoint);
            });
        }

        public virtual PaymentUrlBuilder GenerateBIP21(string cryptoInfoAddress, Money cryptoInfoDue)
        {
            var builder = new PaymentUrlBuilder(this.NBitcoinNetwork.UriScheme);
            builder.Host = cryptoInfoAddress;
            if (cryptoInfoDue != null && cryptoInfoDue != Money.Zero)
            {
                builder.QueryParams.Add("amount", cryptoInfoDue.ToString(false, true));
            }
            return builder;
        }

        public virtual List<TransactionInformation> FilterValidTransactions(List<TransactionInformation> transactionInformationSet)
        {
            return transactionInformationSet;
        }
    }

    public abstract class BTCPayNetworkBase
    {
        private string _blockExplorerLink;
        public bool ShowSyncSummary { get; set; } = true;
        public string CryptoCode { get; set; }

        public string BlockExplorerLink
        {
            get => _blockExplorerLink;
            set
            {
                if (string.IsNullOrEmpty(BlockExplorerLinkDefault))
                {
                    BlockExplorerLinkDefault = value;
                }

                _blockExplorerLink = value;
            }
        }

        public string BlockExplorerLinkDefault { get; set; }
        public string DisplayName { get; set; }
        public int Divisibility { get; set; } = 8;
        public bool IsBTC
        {
            get
            {
                return CryptoCode == "BTC";
            }
        }

        public string CryptoImagePath { get; set; }
        public string[] DefaultRateRules { get; set; } = Array.Empty<string>();

        public override string ToString()
        {
            return CryptoCode;
        }

        public virtual T ToObject<T>(string json)
        {
            return NBitcoin.JsonConverters.Serializer.ToObject<T>(json, null);
        }

        public virtual string ToString<T>(T obj)
        {
            return NBitcoin.JsonConverters.Serializer.ToString(obj, null);
        }
    }
}
