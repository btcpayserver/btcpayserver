using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitECash()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("XEC");

            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "eCash",
                BlockExplorerLink = NetworkType == ChainName.Mainnet ? "https://explorer.bitcoinabc.org/tx{0}" :
                                    "https://texplorer.bitcoinabc.org/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                CryptoImagePath = "imlegacy/ecash.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("899'") : new KeyPath("0'"),
                SupportRBF = false,
                SupportPayJoin = false,
                VaultSupported = false,
                DefaultRateRules = new[]
                {
                    "XEC_X = XEC_BTC * BTC_X",
                    "XEC_BTC = coingecko(XEC_BTC)"
                },
            });
        }
    }
}
