﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitLitecoin()
        {
            NBXplorer.Altcoins.Litecoin.Networks.EnsureRegistered();
            var ltcRate = new CoinAverageRateProvider("LTC");

            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("LTC");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                BlockExplorerLink = NBXplorerNetworkProvider.ChainType == ChainType.Main ? "https://live.blockcypher.com/ltc/tx/{0}/" : "http://explorer.litecointools.com/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "litecoin",
                DefaultRateProvider = ltcRate,
                CryptoImagePath = "imlegacy/litecoin-symbol.svg",
                LightningImagePath = "imlegacy/ltc-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NBXplorerNetworkProvider.ChainType),
                CoinType = NBXplorerNetworkProvider.ChainType == ChainType.Main ? new KeyPath("2'") : new KeyPath("3'"),
                CLightningNetworkName = ChainType == ChainType.Main ? "litecoin" :
                                        ChainType == ChainType.Test ? "litecoin-testnet" : null
            });
        }
    }
}
