#nullable enable
using System;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Configuration;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Zcash.Configuration;
using BTCPayServer.Plugins.Zcash.Payments;
using BTCPayServer.Plugins.Zcash.Services;
using BTCPayServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Plugins.Zcash
{
    public class ZcashPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier => "BTCPayServer.Plugins.Zcash";
        public override string Name => "Zcash";
        public override string Description => "Add Zcash support";

        public override void Execute(IServiceCollection applicationBuilder)
        {
            
            var services = (PluginServiceCollection)applicationBuilder;
            if (!services.BootstrapServices.GetRequiredService<SelectedChains>().Contains("ZEC"))
                return;
            var chainName = services.BootstrapServices.GetRequiredService<NBXplorerNetworkProvider>().NetworkType;

            var network = new ZcashLikeSpecificBtcPayNetwork()
            {
                CryptoCode = "ZEC",
                DisplayName = "Zcash",
                Divisibility = 8,
                DefaultRateRules = new[]
                {
                    "ZEC_X = ZEC_BTC * BTC_X",
                    "ZEC_BTC = kraken(ZEC_BTC)"
                },
                CryptoImagePath = "/imlegacy/zcash.png",
                UriScheme = "zcash"
            };
            var blockExplorerLink = chainName == ChainName.Mainnet
                ? "https://www.exploreZcash.com/transaction/{0}"
                : "https://testnet.xmrchain.net/tx/{0}";
            services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(new PaymentMethodId("ZEC", ZcashPaymentType.Instance), new DefaultTransactionLinkProvider(blockExplorerLink));
           
            
             services.AddSingleton(serviceProvider =>
            {
                var configuration = serviceProvider.GetService<IConfiguration>();
                var btcPayNetworkProvider = serviceProvider.GetService<BTCPayNetworkProvider>();
                var result = new ZcashLikeConfiguration();

                var supportedNetworks = btcPayNetworkProvider.GetAll()
                    .OfType<ZcashLikeSpecificBtcPayNetwork>();

                foreach (var ZcashLikeSpecificBtcPayNetwork in supportedNetworks)
                {
                    var daemonUri =
                        configuration.GetOrDefault<Uri>($"{ZcashLikeSpecificBtcPayNetwork.CryptoCode}_daemon_uri",
                            null);
                    var walletDaemonUri =
                        configuration.GetOrDefault<Uri>(
                            $"{ZcashLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_uri", null);
                    var walletDaemonWalletDirectory =
                        configuration.GetOrDefault<string>(
                            $"{ZcashLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_walletdir", null);
                    if (daemonUri == null || walletDaemonUri == null)
                    {
                        throw new ConfigException($"{ZcashLikeSpecificBtcPayNetwork.CryptoCode} is misconfigured");
                    }

                    result.ZcashLikeConfigurationItems.Add(ZcashLikeSpecificBtcPayNetwork.CryptoCode, new ZcashLikeConfigurationItem()
                    {
                        DaemonRpcUri = daemonUri,
                        InternalWalletRpcUri = walletDaemonUri,
                        WalletDirectory = walletDaemonWalletDirectory
                    });
                }
                return result;
            });
            services.AddSingleton<ZcashRPCProvider>();
            services.AddHostedService<ZcashLikeSummaryUpdaterHostedService>();
            services.AddHostedService<ZcashListener>();
            services.AddSingleton<ZcashLikePaymentMethodHandler>();
            services.AddSingleton<IPaymentMethodHandler>(provider => provider.GetService<ZcashLikePaymentMethodHandler>());
            services.AddSingleton<IUIExtension>(new UIExtension("Zcash/StoreNavZcashExtension",  "store-nav"));
            services.AddSingleton<ISyncSummaryProvider, ZcashSyncSummaryProvider>();

        }
    }
}
