#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Configuration;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Monero.Configuration;
using BTCPayServer.Plugins.Monero.Payments;
using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Plugins.Monero
{
    public class MoneroPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier => "BTCPayServer.Plugins.Monero";
        public override string Name => "Monero";
        public override string Description => "Add Monero support";

        public override void Execute(IServiceCollection applicationBuilder)
        {
            
            var services = (PluginServiceCollection)applicationBuilder;
            if (!services.BootstrapServices.GetRequiredService<SelectedChains>().Contains("XMR"))
                return;
            var chainName = services.BootstrapServices.GetRequiredService<NBXplorerNetworkProvider>().NetworkType;
            var network =new MoneroLikeSpecificBtcPayNetwork()
            {
                CryptoCode = "XMR",
                DisplayName = "Monero",
                Divisibility = 12,
                DefaultRateRules = new[]
                {
                    "XMR_X = XMR_BTC * BTC_X",
                    "XMR_BTC = kraken(XMR_BTC)"
                },
                CryptoImagePath = "/imlegacy/monero.svg",
                UriScheme = "monero"
            };
            var blockExplorerLink = chainName == ChainName.Mainnet
                ? "https://www.exploremonero.com/transaction/{0}"
                : "https://testnet.xmrchain.net/tx/{0}";
            services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(new PaymentMethodId("XMR", MoneroPaymentType.Instance), new DefaultTransactionLinkProvider(blockExplorerLink));
            
             services.AddSingleton(serviceProvider =>
            {
                var configuration = serviceProvider.GetService<IConfiguration>();
                var btcPayNetworkProvider = serviceProvider.GetService<BTCPayNetworkProvider>();
                var result = new MoneroLikeConfiguration();

                var supportedNetworks = btcPayNetworkProvider.GetAll()
                    .OfType<MoneroLikeSpecificBtcPayNetwork>();

                foreach (var moneroLikeSpecificBtcPayNetwork in supportedNetworks)
                {
                    var daemonUri =
                        configuration.GetOrDefault<Uri>($"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_daemon_uri",
                            null);
                    var walletDaemonUri =
                        configuration.GetOrDefault<Uri>(
                            $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_uri", null);
                    var walletDaemonWalletDirectory =
                        configuration.GetOrDefault<string>(
                            $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_walletdir", null);
                    var daemonUsername =
                        configuration.GetOrDefault<string>(
                            $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_daemon_username", null);
                    var daemonPassword =
                        configuration.GetOrDefault<string>(
                            $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_daemon_password", null);
                    if (daemonUri == null || walletDaemonUri == null)
                    {
                        throw new ConfigException($"{moneroLikeSpecificBtcPayNetwork.CryptoCode} is misconfigured");
                    }

                    result.MoneroLikeConfigurationItems.Add(moneroLikeSpecificBtcPayNetwork.CryptoCode,
                        new MoneroLikeConfigurationItem()
                        {
                            DaemonRpcUri = daemonUri,
                            Username = daemonUsername,
                            Password = daemonPassword,
                            InternalWalletRpcUri = walletDaemonUri,
                            WalletDirectory = walletDaemonWalletDirectory
                        });
                }

                return result;

            });
            services.AddHttpClient("XMRclient")
                .ConfigurePrimaryHttpMessageHandler(provider =>
                {
                    var configuration = provider.GetRequiredService<MoneroLikeConfiguration>();
                    if(!configuration.MoneroLikeConfigurationItems.TryGetValue("XMR", out var xmrConfig) || xmrConfig.Username is null || xmrConfig.Password is null){
                        return new HttpClientHandler();
                    }
                    return new HttpClientHandler
                    {
                        Credentials = new NetworkCredential(xmrConfig.Username, xmrConfig.Password),
                        PreAuthenticate = true
                    };
                });
            services.AddSingleton<MoneroRPCProvider>();
            services.AddHostedService<MoneroLikeSummaryUpdaterHostedService>();
            services.AddHostedService<MoneroListener>();
            services.AddSingleton<MoneroLikePaymentMethodHandler>();
            services.AddSingleton<IPaymentMethodHandler>(provider => provider.GetService<MoneroLikePaymentMethodHandler>());
            services.AddSingleton<IUIExtension>(new UIExtension("Monero/StoreNavMoneroExtension",  "store-nav"));
            services.AddSingleton<IUIExtension>(new UIExtension("Monero/StoreWalletsNavMoneroExtension",  "store-wallets-nav"));
            services.AddSingleton<ISyncSummaryProvider, MoneroSyncSummaryProvider>();
        }
    }
}
