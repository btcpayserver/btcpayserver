#if ALTCOINS
using NBitcoin;
using NBitcoin.Altcoins;
using NBitcoin.Altcoins.Elements;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitLiquid()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("LBTC");
            Add(new ElementsBTCPayNetwork()
            {
                AssetId = NetworkType == ChainName.Mainnet ? ElementsParams<Liquid>.PeggedAssetId : ElementsParams<Liquid.LiquidRegtest>.PeggedAssetId,
                CryptoCode = "LBTC",
                NetworkCryptoCode = "LBTC",
                DisplayName = "Liquid Bitcoin",
                DefaultRateRules = new[]
                {
                    "LBTC_X = LBTC_BTC * BTC_X",
                    "LBTC_BTC = 1",
                },
                BlockExplorerLink = NetworkType == ChainName.Mainnet ? "https://blockstream.info/liquid/tx/{0}" : "https://blockstream.info/testnet/liquid/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "liquidnetwork",
                CryptoImagePath = "imlegacy/liquid.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("1776'") : new KeyPath("1'"),
                SupportRBF = true
            });
        }
    }


}
#endif
