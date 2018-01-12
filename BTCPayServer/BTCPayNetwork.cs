using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public class BTCPayDefaultSettings
    {
        static BTCPayDefaultSettings()
        {
            _Settings = new Dictionary<ChainType, BTCPayDefaultSettings>();
            foreach (var chainType in new[] { ChainType.Main, ChainType.Test, ChainType.Regtest })
            {
                var btcNetwork = (chainType == ChainType.Main ? Network.Main :
                                  chainType == ChainType.Regtest ? Network.RegTest :
                                  chainType == ChainType.Test ? Network.TestNet : throw new NotSupportedException(chainType.ToString()));

                var settings = new BTCPayDefaultSettings();
                _Settings.Add(chainType, settings);
                settings.ChainType = chainType;
                settings.DefaultDataDirectory = StandardConfiguration.DefaultDataDirectory.GetDirectory("BTCPayServer", btcNetwork.Name);
                settings.DefaultConfigurationFile = Path.Combine(settings.DefaultDataDirectory, "settings.config");
                settings.DefaultPort = (chainType == ChainType.Main ? 23000 :
                                                      chainType == ChainType.Regtest ? 23002 :
                                                      chainType == ChainType.Test ? 23001 : throw new NotSupportedException(chainType.ToString()));
            }
        }

        static Dictionary<ChainType, BTCPayDefaultSettings> _Settings;

        public static BTCPayDefaultSettings GetDefaultSettings(ChainType chainType)
        {
            return _Settings[chainType];
        }

        public string DefaultDataDirectory { get; set; }
        public string DefaultConfigurationFile { get; set; }
        public ChainType ChainType { get; internal set; }
        public int DefaultPort { get; set; }
    }
    public class BTCPayNetwork
    {
        public Network NBitcoinNetwork { get; set; }
        public string CryptoCode { get; internal set; }
        public string BlockExplorerLink { get; internal set; }
        public string UriScheme { get; internal set; }
        public IRateProvider DefaultRateProvider { get; set; }

        [Obsolete("Should not be needed")]
        public bool IsBTC
        {
            get
            {
                return CryptoCode == "BTC";
            }
        }

        public string CryptoImagePath { get; set; }
        public NBXplorer.NBXplorerNetwork NBXplorerNetwork { get; set; }


        public BTCPayDefaultSettings DefaultSettings { get; set; }
        public override string ToString()
        {
            return CryptoCode;
        }
    }
}
