using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitLiquidAssets()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("LBTC");
            Add(new ElementsBTCPayNetwork()
            {
                CryptoCode = "USDt",
                NetworkCryptoCode = "LBTC",
                ShowSyncSummary = false,
                DefaultRateRules = new[]
                {
                    "USDT_UST = 1",
                    "USDT_X = USDT_BTC * BTC_X",
                    "USDT_BTC = bitfinex(UST_BTC)",
                },
                AssetId = new uint256("ce091c998b83c78bb71a632313ba3760f1763d9cfcffae02258ffa9865a37bd2"),
                DisplayName = "Liquid Tether",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://blockstream.info/liquid/tx/{0}" : "https://blockstream.info/testnet/liquid/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "liquidnetwork",
                CryptoImagePath = "imlegacy/liquid-tether.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("1776'") : new KeyPath("1'"),
                SupportRBF = true
            });
            
            Add(new ElementsBTCPayNetwork()
            {
                CryptoCode = "ETB",
                NetworkCryptoCode = "LBTC",
                ShowSyncSummary = false,
                DefaultRateRules = new[]
                {
                    
                    "ETB_X = ETB_BTC * BTC_X",
                    "ETB_BTC = bitpay(ETB_BTC)"
                },
                Divisibility = 2,
                AssetId = new uint256("aa775044c32a7df391902b3659f46dfe004ccb2644ce2ddc7dba31e889391caf"),
                DisplayName = "Ethiopian Birr",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://blockstream.info/liquid/tx/{0}" : "https://blockstream.info/testnet/liquid/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "liquidnetwork",
                CryptoImagePath = "imlegacy/etb.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("1776'") : new KeyPath("1'"),
                SupportRBF = true
            });
        }
    }


}
