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
                DisplayName = "BGold",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://explorer.bitcoingold.org/insight/tx/{0}/" : "https://test-explorer.bitcoingold.org/insight/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "bitcoingold",
                DefaultRateRules = new[]
                {
                    "BTG_X = BTG_BTC * BTC_X",
                    "BTG_BTC = bitfinex(BTG_BTC)",
                },
                CryptoImagePath = "imlegacy/btg.svg",
                LightningImagePath = "imlegacy/btg-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("156'") : new KeyPath("1'")
            });
        }
    }
}
