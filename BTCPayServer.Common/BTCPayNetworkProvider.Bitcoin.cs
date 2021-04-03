using System.Collections.Generic;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitBitcoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("BTC");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Bitcoin",
                BlockExplorerLink = NetworkType == ChainName.Mainnet ? "https://blockstream.info/tx/{0}" :
                                    NetworkType == Bitcoin.Instance.Signet.ChainName ? "https://explorer.bc-2.jp/tx/{0}"
                                    : "https://blockstream.info/testnet/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "bitcoin",
                CryptoImagePath = "imlegacy/bitcoin.svg",
                LightningImagePath = "imlegacy/bitcoin-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("0'") : new KeyPath("1'"),
                SupportRBF = true,
                SupportPayJoin = true,
                VaultSupported = true,
                //https://github.com/spesmilo/electrum/blob/11733d6bc271646a00b69ff07657119598874da4/electrum/constants.py
                ElectrumMapping = NetworkType == ChainName.Mainnet
                    ? new Dictionary<uint, DerivationType>()
                    {
                        {0x0488b21eU, DerivationType.Legacy }, // xpub
                        {0x049d7cb2U, DerivationType.SegwitP2SH }, // ypub
                        {0x04b24746U, DerivationType.Segwit }, //zpub
                    }
                    : new Dictionary<uint, DerivationType>()
                    {
                        {0x043587cfU, DerivationType.Legacy}, // tpub
                        {0x044a5262U, DerivationType.SegwitP2SH}, // upub
                        {0x045f1cf6U, DerivationType.Segwit} // vpub
                    }
            });
        }
    }
}
