using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using NBitcoin;
using NBitpayClient;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitBitcoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("BTC");
            var coinaverage = new CoinAverageRateProviderDescription("BTC");
            var bitpay = new BitpayRateProviderDescription();
            var btcRate = new FallbackRateProviderDescription(new RateProviderDescription[] { coinaverage, bitpay });
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                BlockExplorerLink = NBXplorerNetworkProvider.ChainType == ChainType.Main ? "https://www.smartbit.com.au/tx/{0}" : "https://testnet.smartbit.com.au/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "bitcoin",
                DefaultRateProvider = btcRate,
                CryptoImagePath = "imlegacy/bitcoin-symbol.svg",
                LightningImagePath = "imlegacy/btc-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NBXplorerNetworkProvider.ChainType),
                CoinType = NBXplorerNetworkProvider.ChainType == ChainType.Main ? new KeyPath("0'") : new KeyPath("1'")
            });
        }
    }
}
