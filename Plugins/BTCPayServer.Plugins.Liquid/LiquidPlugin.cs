#nullable enable
using System;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Liquid.Services;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Altcoins.Elements;
using NBXplorer;

namespace BTCPayServer.Plugins.Liquid
{
    public class LiquidPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier => "BTCPayServer.Plugins.Liquid";
        public override string Name => "Liquid";
        public override string Description => "Add Liquid support";

        public override void Execute(IServiceCollection applicationBuilder)
        {
            var services = (PluginServiceCollection)applicationBuilder;
            var selectedChains = services.BootstrapServices.GetRequiredService<SelectedChains>();
            if (!selectedChains.Contains("LBTC"))
                return;
            var networkProvider = services.BootstrapServices.GetRequiredService<NBXplorerNetworkProvider>();

            var network = networkProvider.GetLBTC();
            if (network == null)
                return;

            services.AddSingleton<IUIExtension>(new UIExtension("LiquidNav", "store-integrations-nav"));
            services.AddSingleton<IUIExtension>(new UIExtension("OnChainWalletSetupLiquidExtension",
                "onchain-wallet-setup-post-body"));
            services.AddSingleton<IUIExtension>(new UIExtension("CustomLiquidAssetsNavExtension", "server-nav"));
            services.AddSingleton<IUIExtension>(new UIExtension("StoreNavLiquidExtension", "store-nav"));

            var repository =
                ActivatorUtilities.CreateInstance<CustomLiquidAssetsRepository>(services.BootstrapServices);

            services.AddSingleton(repository);


            var chainName = networkProvider.NetworkType;

            var blockExplorerLink = chainName == ChainName.Mainnet
                ? "https://liquid.network/tx/{0}"
                : "https://liquid.network/testnet/tx/{0}";
            var linkProvider = new DefaultTransactionLinkProvider(blockExplorerLink);

            var liquidNetwork = (ElementsBTCPayNetwork)new ElementsBTCPayNetwork()
            {
                AssetId =
                    chainName == ChainName.Mainnet
                        ? ElementsParams<NBitcoin.Altcoins.Liquid>.PeggedAssetId
                        : ElementsParams<NBitcoin.Altcoins.Liquid.LiquidRegtest>.PeggedAssetId,
                CryptoCode = "LBTC",
                NetworkCryptoCode = "LBTC",
                DisplayName = "Liquid Bitcoin",
                DefaultRateRules = new[] {"LBTC_X = LBTC_BTC * BTC_X", "LBTC_BTC = 1",},
                NBXplorerNetwork = network,
                CryptoImagePath = "imlegacy/liquid.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(chainName),
                CoinType = chainName == ChainName.Mainnet ? new KeyPath("1776'") : new KeyPath("1'"),
                SupportRBF = true
            }.SetDefaultElectrumMapping(chainName);

            services.AddBTCPayNetwork(liquidNetwork)
                .AddTransactionLinkProvider(new PaymentMethodId("LBTC", PaymentTypes.BTCLike), linkProvider);

            services.AddBTCPayNetwork(new ElementsBTCPayNetwork()
                {
                    CryptoCode = "USDt",
                    NetworkCryptoCode = "LBTC",
                    ShowSyncSummary = false,
                    DefaultRateRules =
                        new[] {"USDT_UST = 1", "USDT_X = USDT_BTC * BTC_X", "USDT_BTC = bitfinex(UST_BTC)",},
                    AssetId = new uint256("ce091c998b83c78bb71a632313ba3760f1763d9cfcffae02258ffa9865a37bd2"),
                    DisplayName = "Liquid Tether",
                    NBXplorerNetwork = network,
                    CryptoImagePath = "imlegacy/liquid-tether.svg",
                    DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(chainName),
                    CoinType = chainName == ChainName.Mainnet ? new KeyPath("1776'") : new KeyPath("1'"),
                    SupportRBF = true,
                    SupportLightning = false
                }.SetDefaultElectrumMapping(chainName))
                .AddTransactionLinkProvider(new PaymentMethodId("USDt", PaymentTypes.BTCLike), linkProvider);

            services.AddBTCPayNetwork(new ElementsBTCPayNetwork()
                {
                    CryptoCode = "ETB",
                    NetworkCryptoCode = "LBTC",
                    ShowSyncSummary = false,
                    DefaultRateRules = new[] {"ETB_X = ETB_BTC * BTC_X", "ETB_BTC = bitpay(ETB_BTC)"},
                    Divisibility = 2,
                    AssetId = new uint256("aa775044c32a7df391902b3659f46dfe004ccb2644ce2ddc7dba31e889391caf"),
                    DisplayName = "Ethiopian Birr",
                    NBXplorerNetwork = network,
                    CryptoImagePath = "imlegacy/etb.png",
                    DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(chainName),
                    CoinType = chainName == ChainName.Mainnet ? new KeyPath("1776'") : new KeyPath("1'"),
                    SupportRBF = true,
                    SupportLightning = false
                }.SetDefaultElectrumMapping(chainName))
                .AddTransactionLinkProvider(new PaymentMethodId("ETB", PaymentTypes.BTCLike), linkProvider);
            services.AddBTCPayNetwork(new ElementsBTCPayNetwork()
                {
                    CryptoCode = "LCAD",
                    NetworkCryptoCode = "LBTC",
                    ShowSyncSummary = false,
                    DefaultRateRules =
                        new[]
                        {
                            "LCAD_CAD = 1", "LCAD_X = CAD_BTC * BTC_X", "LCAD_BTC = bylls(CAD_BTC)",
                            "CAD_BTC = LCAD_BTC"
                        },
                    AssetId = new uint256("0e99c1a6da379d1f4151fb9df90449d40d0608f6cb33a5bcbfc8c265f42bab0a"),
                    DisplayName = "Liquid CAD",
                    NBXplorerNetwork = network,
                    CryptoImagePath = "imlegacy/lcad.png",
                    DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(chainName),
                    CoinType = chainName == ChainName.Mainnet ? new KeyPath("1776'") : new KeyPath("1'"),
                    SupportRBF = true,
                    SupportLightning = false
                }.SetDefaultElectrumMapping(chainName))
                .AddTransactionLinkProvider(new PaymentMethodId("LCAD", PaymentTypes.BTCLike), linkProvider);
            selectedChains.Add("USDt");
            selectedChains.Add("LCAD");
            selectedChains.Add("ETB");


            var settings = repository.Get();
            var newCryptoCodes = settings.Items.Select(configuration => configuration.CryptoCode).ToArray();
            foreach (var configuration in settings.Items)
            {
                var newNetwork = new ElementsBTCPayNetwork()
                {
                    CryptoCode = configuration.CryptoCode
                        .Replace("-", "")
                        .Replace("_", ""),
                    DefaultRateRules = configuration.DefaultRateRules ?? Array.Empty<string>(),
                    AssetId = uint256.Parse(configuration.AssetId),
                    Divisibility = configuration.Divisibility,
                    DisplayName = configuration.DisplayName,
                    CryptoImagePath = configuration.CryptoImagePath,
                    NetworkCryptoCode = liquidNetwork.NetworkCryptoCode,
                    DefaultSettings = liquidNetwork.DefaultSettings,
                    ElectrumMapping = liquidNetwork.ElectrumMapping,
                    ReadonlyWallet = liquidNetwork.ReadonlyWallet,
                    SupportLightning = false,
                    SupportPayJoin = false,
                    ShowSyncSummary = false,
                    WalletSupported = liquidNetwork.WalletSupported,
                    LightningImagePath = "",
                    NBXplorerNetwork = liquidNetwork.NBXplorerNetwork,
                    CoinType = liquidNetwork.CoinType,
                    VaultSupported = liquidNetwork.VaultSupported,
                    MaxTrackedConfirmation = liquidNetwork.MaxTrackedConfirmation,
                    SupportRBF = liquidNetwork.SupportRBF
                };
                services.AddBTCPayNetwork(newNetwork)
                    .AddTransactionLinkProvider(new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike),
                        linkProvider);
                selectedChains.Add(network.CryptoCode);
            }

            services.BootstrapServices.GetService<ILogger<LiquidPlugin>>().LogInformation(
                $"Loaded {newCryptoCodes.Length} " +
                $"{(!newCryptoCodes.Any() ? string.Empty : $"({string.Join(',', newCryptoCodes)})")} additional liquid assets");


            if (chainName == ChainName.Regtest)
            {
                services.AddStartupTask<LiquidRegtestAssetSetup>();
            }
        }
    }
}
