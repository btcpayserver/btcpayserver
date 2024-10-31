using System.Threading;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;

namespace BTCPayServer.Plugins.Altcoins;
public partial class AltcoinsPlugin
{
    private void InitUSDT(IServiceCollection services, SelectedChains selectedChains, NBXplorer.NBXplorerNetwork nbxplorerNetwork)
    {
        var network = new ElementsBTCPayNetwork()
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
            NBXplorerNetwork = nbxplorerNetwork,
            CryptoImagePath = "imlegacy/liquid-tether.svg",
            DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(ChainName),
            CoinType = ChainName == ChainName.Mainnet ? new KeyPath("1776'") : new KeyPath("1'"),
            SupportRBF = true,
            SupportLightning = false,
            SupportPayJoin = false,
            VaultSupported = false,
            ReadonlyWallet = true
        }.SetDefaultElectrumMapping(ChainName);
        services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(PaymentTypes.CHAIN.GetPaymentMethodId("USDt"), new DefaultTransactionLinkProvider(LiquidBlockExplorer));
        services.AddCurrencyData(new CurrencyData()
        {
            Code = "USDt",
            Name = "USDt",
            Divisibility = 8,
            Symbol = null,
            Crypto = true
        });
        selectedChains.Add("LBTC");
    }

    string LiquidBlockExplorer => ChainName == ChainName.Mainnet ? "https://liquid.network/tx/{0}" : "https://liquid.network/testnet/tx/{0}";
    private void InitLCAD(IServiceCollection services, SelectedChains selectedChains, NBXplorer.NBXplorerNetwork nbxplorerNetwork)
    {
        var network = new ElementsBTCPayNetwork()
        {
            CryptoCode = "LCAD",
            NetworkCryptoCode = "LBTC",
            ShowSyncSummary = false,
            DefaultRateRules = new[]
                  {
                    "LCAD_CAD = 1",
                    "LCAD_X = CAD_BTC * BTC_X",
                    "LCAD_BTC = bylls(CAD_BTC)",
                    "CAD_BTC = LCAD_BTC"
                },
            AssetId = new uint256("0e99c1a6da379d1f4151fb9df90449d40d0608f6cb33a5bcbfc8c265f42bab0a"),
            DisplayName = "Liquid CAD",
            NBXplorerNetwork = nbxplorerNetwork,
            CryptoImagePath = "imlegacy/lcad.png",
            DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(ChainName),
            CoinType = ChainName == ChainName.Mainnet ? new KeyPath("1776'") : new KeyPath("1'"),
            SupportRBF = true,            
            SupportLightning = false,
            SupportPayJoin = false,
            VaultSupported = false,
            ReadonlyWallet = true
        }.SetDefaultElectrumMapping(ChainName);
        services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(PaymentTypes.CHAIN.GetPaymentMethodId("LCAD"), new DefaultTransactionLinkProvider(LiquidBlockExplorer));
        selectedChains.Add("LBTC");
    }

}
