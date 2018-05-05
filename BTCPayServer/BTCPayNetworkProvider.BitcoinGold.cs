using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitBitcoinGold()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("BTG");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://explorer.bitcoingold.org/insight/tx/{0}/" : "https://test-explorer.bitcoingold.org/insight/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "bitcoingold",
                DefaultRateRules = new[]
                {
                    "BTG_X = BTG_BTC * BTC_X",
                    "BTG_BTC = bitfinex(BTG_BTC)",
                    "BTG_USD = bitfinex(BTG_USD)",
                },
                CryptoImagePath = "imlegacy/btg-symbol.svg",
                LightningImagePath = "imlegacy/btg-symbol.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("156'") : new KeyPath("1'")
            });
        }
    }
}
