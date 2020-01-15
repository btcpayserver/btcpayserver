using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBXplorer;

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
                SupportRBF = true,
                //https://github.com/spesmilo/electrum/blob/11733d6bc271646a00b69ff07657119598874da4/electrum/constants.py
                ElectrumMapping = NetworkType == NetworkType.Mainnet
                    ? new Dictionary<uint, DerivationType>()
                    {
                        {0x0488b21eU, DerivationType.Legacy }, // xpub
                        {0x049d7cb2U, DerivationType.SegwitP2SH }, // ypub
                        {0x4b24746U, DerivationType.Segwit }, //zpub
                    }
                    : new Dictionary<uint, DerivationType>()
                    {
                        {0x043587cfU, DerivationType.Legacy},
                        {0x044a5262U, DerivationType.SegwitP2SH},
                        {0x045f1cf6U, DerivationType.Segwit}
                    }
            });
        }
    }


}
