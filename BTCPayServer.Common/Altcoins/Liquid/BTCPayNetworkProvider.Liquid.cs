using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Altcoins;
using NBitcoin.Altcoins.Elements;
using NBXplorer;

namespace BTCPayServer
{
    public partial class AltcoinBTCPayNetworkProvider
    {
        public BTCPayNetworkBase InitLiquid(NBXplorerNetworkProvider nbXplorerNetworkProvider)
        {
            var nbxplorerNetwork = nbXplorerNetworkProvider.GetFromCryptoCode("LBTC");
            return new ElementsBTCPayNetwork()
            {
                AssetId = nbXplorerNetworkProvider.NetworkType == NetworkType.Mainnet ? ElementsParams<Liquid>.PeggedAssetId: ElementsParams<Liquid.LiquidRegtest>.PeggedAssetId,
                CryptoCode = "LBTC",
                NetworkCryptoCode = "LBTC",
                DisplayName = "Liquid Bitcoin",
                DefaultRateRules = new[]
                {
                    "LBTC_X = LBTC_BTC * BTC_X",
                    "LBTC_BTC = 1",
                },
                BlockExplorerLink = nbXplorerNetworkProvider.NetworkType == NetworkType.Mainnet ? "https://blockstream.info/liquid/tx/{0}" : "https://blockstream.info/testnet/liquid/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "liquidnetwork",
                CryptoImagePath = "imlegacy/liquid.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(nbXplorerNetworkProvider.NetworkType),
                CoinType = nbXplorerNetworkProvider.NetworkType == NetworkType.Mainnet ? new KeyPath("1776'") : new KeyPath("1'"),
                SupportRBF = true
            };
        }
    }


}
