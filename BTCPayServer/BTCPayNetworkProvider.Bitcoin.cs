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
            var coinaverage = new CoinAverageRateProvider("BTC");
            var bitpay = new BitpayRateProvider(new Bitpay(new Key(), new Uri("https://bitpay.com/")));
            var btcRate = new FallbackRateProvider(new IRateProvider[] { coinaverage, bitpay });
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
                CoinType = NBXplorerNetworkProvider.ChainType == ChainType.Main ? new KeyPath("0'") : new KeyPath("1'"),
                CLightningNetworkName = ChainType == ChainType.Main ? "bitcoin" :
                                        ChainType == ChainType.Test ? "testnet" :
                                        ChainType == ChainType.Regtest ? "regtest" : null
            });
        }
    }
}
