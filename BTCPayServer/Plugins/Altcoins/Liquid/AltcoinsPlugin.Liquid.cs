using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBitcoin.Altcoins;
using NBitcoin.Altcoins.Elements;
using NBXplorer;

namespace BTCPayServer.Plugins.Altcoins;
public partial class AltcoinsPlugin
{
    public void InitLiquid(IServiceCollection services, NBXplorer.NBXplorerNetwork nbxplorerNetwork)
    {
        var network = new ElementsBTCPayNetwork()
        {
            AssetId = ChainName == ChainName.Mainnet ? ElementsParams<Liquid>.PeggedAssetId : ElementsParams<Liquid.LiquidRegtest>.PeggedAssetId,
            CryptoCode = "LBTC",
            NetworkCryptoCode = "LBTC",
            DisplayName = "Liquid Bitcoin",
            DefaultRateRules = new[]
            {
                    "LBTC_X = LBTC_BTC * BTC_X",
                    "LBTC_BTC = 1",
            },
            NBXplorerNetwork = nbxplorerNetwork,
            CryptoImagePath = "imlegacy/liquid.png",
            DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(ChainName),
            CoinType = ChainName == ChainName.Mainnet ? new KeyPath("1776'") : new KeyPath("1'"),
            SupportRBF = true
        }.SetDefaultElectrumMapping(ChainName);

        var blockExplorerLink = ChainName == ChainName.Mainnet ? "https://liquid.network/tx/{0}" : "https://liquid.network/testnet/tx/{0}";
        services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(PaymentTypes.CHAIN.GetPaymentMethodId(nbxplorerNetwork.CryptoCode), new DefaultTransactionLinkProvider(blockExplorerLink));
    }
}
