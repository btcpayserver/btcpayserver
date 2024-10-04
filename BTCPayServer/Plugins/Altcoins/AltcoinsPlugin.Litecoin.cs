using System.Collections.Generic;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Plugins.Altcoins;

public partial class AltcoinsPlugin
{
    public void InitLitecoin(IServiceCollection services)
    {
        var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("LTC");
        var network = new BTCPayNetwork()
        {
            CryptoCode = nbxplorerNetwork.CryptoCode,
            DisplayName = "Litecoin",
            NBXplorerNetwork = nbxplorerNetwork,
            DefaultRateRules = new[]
            {
                    "LTC_X = LTC_BTC * BTC_X",
                    "LTC_BTC = coingecko(LTC_BTC)"
                },
            CryptoImagePath = "imlegacy/litecoin.svg",
            LightningImagePath = "imlegacy/litecoin-lightning.svg",
            DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(ChainName),
            CoinType = ChainName == ChainName.Mainnet ? new KeyPath("2'") : new KeyPath("1'"),
            //https://github.com/pooler/electrum-ltc/blob/0d6989a9d2fb2edbea421c116e49d1015c7c5a91/electrum_ltc/constants.py
            ElectrumMapping = ChainName == ChainName.Mainnet
                ? new Dictionary<uint, DerivationType>()
                {
                        {0x0488b21eU, DerivationType.Legacy },
                        {0x049d7cb2U, DerivationType.SegwitP2SH },
                        {0x04b24746U, DerivationType.Segwit },
                }
                : new Dictionary<uint, DerivationType>()
                {
                        {0x043587cfU, DerivationType.Legacy },
                        {0x044a5262U, DerivationType.SegwitP2SH },
                        {0x045f1cf6U, DerivationType.Segwit }
                }
        };
        var blockExplorerLinks = ChainName == ChainName.Mainnet
                ? "https://live.blockcypher.com/ltc/tx/{0}/"
                : "http://explorer.litecointools.com/tx/{0}";
        services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(PaymentTypes.CHAIN.GetPaymentMethodId(nbxplorerNetwork.CryptoCode), new DefaultTransactionLinkProvider(blockExplorerLinks));
    }
}

