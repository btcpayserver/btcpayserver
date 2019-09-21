﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBXplorer;
using Newtonsoft.Json;

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

    public class BTCPayNetwork:BTCPayNetworkBase
    {
        public Network NBitcoinNetwork { get; set; }
        public NBXplorer.NBXplorerNetwork NBXplorerNetwork { get; set; }
        public bool SupportRBF { get; internal set; }
        public string LightningImagePath { get; set; }
        public BTCPayDefaultSettings DefaultSettings { get; set; }
        public KeyPath CoinType { get; internal set; }
        
        public Dictionary<uint, DerivationType> ElectrumMapping = new Dictionary<uint, DerivationType>();

        public int MaxTrackedConfirmation { get; internal set; } = 6;
        public string UriScheme { get; internal set; }
        public KeyPath GetRootKeyPath(DerivationType type)
        {
            KeyPath baseKey;
            if (!NBitcoinNetwork.Consensus.SupportSegwit)
            {
                baseKey = new KeyPath("44'");
            }
            else
            {
                switch (type)
                {
                    case DerivationType.Legacy:
                        baseKey = new KeyPath("44'");
                        break;
                    case DerivationType.SegwitP2SH:
                        baseKey = new KeyPath("49'");
                        break;
                    case DerivationType.Segwit:
                        baseKey = new KeyPath("84'");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(type), type, null);
                }
            }
            return baseKey
                .Derive(CoinType);
        }

        public KeyPath GetRootKeyPath()
        {
            return new KeyPath(NBitcoinNetwork.Consensus.SupportSegwit ? "49'" : "44'")
                .Derive(CoinType);
        }

        public override T ToObject<T>(string json)
        {
            return NBXplorerNetwork.Serializer.ToObject<T>(json);
        }

        public override string ToString<T>(T obj)
        {
            return NBXplorerNetwork.Serializer.ToString(obj);
        }
    }

    public abstract class BTCPayNetworkBase
    {
        
        public string CryptoCode { get; internal set; }
        public string BlockExplorerLink { get; internal set; }
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
        public string[] DefaultRateRules { get; internal set; } = Array.Empty<string>();
        public override string ToString()
        {
            return CryptoCode;
        }

        public virtual T ToObject<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

        public virtual string ToString<T>(T obj)
        {
            return NBitcoin.JsonConverters.Serializer.ToString(obj, null);
        }
    }
}
