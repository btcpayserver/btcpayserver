using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public class BTCPayDefaultSettings
    {
        static BTCPayDefaultSettings()
        {
            _Settings = new Dictionary<NetworkType, BTCPayDefaultSettings>();
            foreach (var chainType in new[] { NetworkType.Mainnet, NetworkType.Testnet, NetworkType.Regtest })
            {
                var settings = new BTCPayDefaultSettings();
                _Settings.Add(chainType, settings);
                settings.DefaultDataDirectory = StandardConfiguration.DefaultDataDirectory.GetDirectory("BTCPayServer", NBXplorerDefaultSettings.GetFolderName(chainType));
                settings.DefaultConfigurationFile = Path.Combine(settings.DefaultDataDirectory, "settings.config");
                settings.DefaultPort = (chainType == NetworkType.Mainnet ? 23000 :
                                                      chainType == NetworkType.Regtest ? 23002 :
                                                      chainType == NetworkType.Testnet ? 23001 : throw new NotSupportedException(chainType.ToString()));
            }
        }

        static Dictionary<NetworkType, BTCPayDefaultSettings> _Settings;

        public static BTCPayDefaultSettings GetDefaultSettings(NetworkType chainType)
        {
            return _Settings[chainType];
        }

        public string DefaultDataDirectory { get; set; }
        public string DefaultConfigurationFile { get; set; }
        public int DefaultPort { get; set; }
    }
    public class BTCPayNetwork
    {
        public Network NBitcoinNetwork { get; set; }
        public string CryptoCode { get; internal set; }
        public string BlockExplorerLink { get; internal set; }
        public string UriScheme { get; internal set; }
        public Money MinFee { get; internal set; }
        public string DisplayName { get; set; }

        [Obsolete("Should not be needed")]
        public bool IsBTC
        {
            get
            {
                return CryptoCode == "BTC";
            }
        }

        public string CryptoImagePath { get; set; }
        public string LightningImagePath { get; set; }
        public NBXplorer.NBXplorerNetwork NBXplorerNetwork { get; set; }

        public BTCPayDefaultSettings DefaultSettings { get; set; }
        public KeyPath CoinType { get; internal set; }
        public int MaxTrackedConfirmation { get; internal set; } = 6;
        public string[] DefaultRateRules { get; internal set; } = Array.Empty<string>();

        public override string ToString()
        {
            return CryptoCode;
        }

        internal KeyPath GetRootKeyPath()
        {
            return new KeyPath(NBitcoinNetwork.Consensus.SupportSegwit ? "49'" : "44'")
                        .Derive(CoinType); 
        }
    }
}
