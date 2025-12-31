using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Hosting;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Tests.Logging;
using BTCPayServer.Tests.Mocks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Tests
{
    public class BTCPayServerTester : IDisposable
    {
        internal readonly string _Directory;
        public ILoggerProvider LoggerProvider { get; }

        ILog TestLogs;
        public BTCPayServerTester(ILog testLogs, ILoggerProvider loggerProvider, string scope)
        {
            this.LoggerProvider = loggerProvider;
            this.TestLogs = testLogs;
            this._Directory = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        public Uri NBXplorerUri
        {
            get; set;
        }

        public Uri LTCNBXplorerUri { get; set; }
        public Uri LBTCNBXplorerUri { get; set; }

        public Uri ServerUri
        {
            get;
            set;
        }
        public Uri ServerUriWithIP
        {
            get;
            set;
        }

        public string Postgres
        {
            get; set;
        }
        public string ExplorerPostgres
        {
            get; set;
        }

        IWebHost _Host;
        public int Port
        {
            get; set;
        }

        public async Task RestartStartupTask<T>()
        {
            var startupTask = GetService<IServiceProvider>().GetServices<Abstractions.Contracts.IStartupTask>()
                .Single(task => task is T);
            await startupTask.ExecuteAsync();
        }

        public bool MockRates { get; set; } = true;
        public string SocksEndpoint { get; set; }

        /// <summary>
        /// This helps testing plugins.
        /// See https://github.com/btcpayserver/btcpayserver/pull/7008
        /// </summary>
        public bool LoadPluginsInDefaultAssemblyContext { get; set; } = true;

        public HashSet<string> Chains { get; set; } = new HashSet<string>() { "BTC" };
        public bool UseLightning { get; set; }
        public bool CheatMode { get; set; } = true;
        public bool DisableRegistration { get; set; } = false;
        public async Task StartAsync()
        {
            if (!Directory.Exists(_Directory))
                Directory.CreateDirectory(_Directory);
            string chain = NBXplorerDefaultSettings.GetFolderName(ChainName.Regtest);
            string chainDirectory = Path.Combine(_Directory, chain);
            if (!Directory.Exists(chainDirectory))
                Directory.CreateDirectory(chainDirectory);

            StringBuilder config = new StringBuilder();
            config.AppendLine($"{chain.ToLowerInvariant()}=1");
            if (InContainer)
            {
                config.AppendLine($"bind=0.0.0.0");
            }
            config.AppendLine($"port={Port}");
            config.AppendLine($"chains={string.Join(',', Chains)}");
            if (Chains.Contains("BTC", StringComparer.OrdinalIgnoreCase))
            {
                config.AppendLine($"btc.explorer.url={NBXplorerUri.AbsoluteUri}");
                config.AppendLine($"btc.explorer.cookiefile=0");
            }

            if (UseLightning)
            {
                config.AppendLine($"btc.lightning={IntegratedLightning}");
                var localLndBackupFile = Path.Combine(_Directory, "walletunlock.json");
                File.Copy(TestUtils.GetTestDataFullPath("LndSeedBackup/walletunlock.json"), localLndBackupFile, true);
                config.AppendLine($"btc.external.lndseedbackup={localLndBackupFile}");
            }

            if (Chains.Contains("LTC", StringComparer.OrdinalIgnoreCase))
            {
                config.AppendLine($"ltc.explorer.url={LTCNBXplorerUri.AbsoluteUri}");
                config.AppendLine($"ltc.explorer.cookiefile=0");
            }
            if (Chains.Contains("LBTC", StringComparer.OrdinalIgnoreCase))
            {
                config.AppendLine($"lbtc.explorer.url={LBTCNBXplorerUri.AbsoluteUri}");
                config.AppendLine($"lbtc.explorer.cookiefile=0");
            }
            if (CheatMode)
                config.AppendLine("cheatmode=1");

            config.AppendLine($"torrcfile={TestUtils.GetTestDataFullPath("Tor/torrc")}");
            config.AppendLine($"socksendpoint={SocksEndpoint}");
            config.AppendLine($"debuglog=debug.log");
            config.AppendLine($"nocsp={NoCSP.ToString().ToLowerInvariant()}");

            if (!string.IsNullOrEmpty(SSHPassword) && string.IsNullOrEmpty(SSHKeyFile))
                config.AppendLine($"sshpassword={SSHPassword}");
            if (!string.IsNullOrEmpty(SSHKeyFile))
                config.AppendLine($"sshkeyfile={SSHKeyFile}");
            if (!string.IsNullOrEmpty(SSHConnection))
                config.AppendLine($"sshconnection={SSHConnection}");

            if (!String.IsNullOrEmpty(Postgres))
                config.AppendLine($"postgres=" + Postgres);

            if (!string.IsNullOrEmpty(ExplorerPostgres))
                config.AppendLine($"explorer.postgres=" + ExplorerPostgres);
            var confPath = Path.Combine(chainDirectory, "settings.config");
            await File.WriteAllTextAsync(confPath, config.ToString());

            ServerUri = new Uri("http://" + HostName + ":" + Port + "/");
            ServerUriWithIP = new Uri("http://127.0.0.1:" + Port + "/");
            HttpClient = new HttpClient();
            HttpClient.BaseAddress = ServerUri;
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            var confBuilder = new DefaultConfiguration() { Logger = LoggerProvider.CreateLogger("Console") }.CreateConfigurationBuilder(new[] { "--datadir", _Directory, "--conf", confPath, "--disable-registration", DisableRegistration ? "true" : "false" });
            // This make sure that tests work outside of this assembly (ie, test project it a plugin)
            confBuilder.SetBasePath(TestUtils.TestDirectory);
#if DEBUG
            confBuilder.AddJsonFile("appsettings.dev.json", true, false);
#endif
            if (LoadPluginsInDefaultAssemblyContext)
                confBuilder.AddInMemoryCollection([new("TEST_RUNNER_ENABLED", "true")]);
            var conf = confBuilder.Build();
            _Host = new WebHostBuilder()
                    .UseDefaultServiceProvider(options =>
                    {
                        options.ValidateScopes = true;
                    })
                    .UseConfiguration(conf)
                    .UseContentRoot(FindBTCPayServerDirectory())
                    .UseWebRoot(Path.Combine(FindBTCPayServerDirectory(), "wwwroot"))
                    .ConfigureServices(s =>
                    {
                        s.AddLogging(l =>
                        {
                            l.AddFilter("System.Net.Http.HttpClient", LogLevel.Critical);
                            l.SetMinimumLevel(LogLevel.Information)
                            .AddFilter("Microsoft", LogLevel.Error)
                            .AddFilter("Microsoft.EntityFrameworkCore.Migrations", LogLevel.Information)
                            .AddFilter("Fido2NetLib.DistributedCacheMetadataService", LogLevel.Error)
                            .AddProvider(LoggerProvider);
                        });
                    })
                    .ConfigureServices(services =>
                    {
                        services.TryAddSingleton<IFeeProviderFactory>(new BTCPayServer.Services.Fees.FixedFeeProvider(new FeeRate(100L, 1)));
                    })
                    .UseKestrel()
                    .UseStartup<Startup>()
                    .Build();
            await _Host.StartWithTasksAsync();

            var urls = _Host.ServerFeatures.Get<IServerAddressesFeature>().Addresses;
            foreach (var url in urls)
            {
                TestLogs.LogInformation("Listening on " + url);
            }
            TestLogs.LogInformation("Server URI " + ServerUri);

            InvoiceRepository = (InvoiceRepository)_Host.Services.GetService(typeof(InvoiceRepository));
            StoreRepository = (StoreRepository)_Host.Services.GetService(typeof(StoreRepository));
            Networks = (BTCPayNetworkProvider)_Host.Services.GetService(typeof(BTCPayNetworkProvider));

            if (MockRates)
            {
                var rateProvider = (RateProviderFactory)_Host.Services.GetService(typeof(RateProviderFactory));
                rateProvider.Providers.Clear();

                coinAverageMock = new MockRateProvider();
                coinAverageMock.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_USD"), new BidAsk(5000m)));
                coinAverageMock.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_EUR"), new BidAsk(4000m)));
                coinAverageMock.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_CAD"), new BidAsk(4500m)));
                coinAverageMock.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_LTC"), new BidAsk(162m)));
                coinAverageMock.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("LTC_USD"), new BidAsk(500m)));
                rateProvider.Providers.Add("coingecko", coinAverageMock);

                var bitflyerMock = new MockRateProvider();
                bitflyerMock.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_JPY"), new BidAsk(700000m)));
                rateProvider.Providers.Add("bitflyer", bitflyerMock);

                var ndax = new MockRateProvider();
                ndax.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_CAD"), new BidAsk(6000m)));
                rateProvider.Providers.Add("ndax", ndax);

                var bitfinex = new MockRateProvider();
                bitfinex.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("UST_BTC"), new BidAsk(0.000136m)));
                rateProvider.Providers.Add("bitfinex", bitfinex);

                var bitpay = new MockRateProvider();
                bitpay.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("ETB_BTC"), new BidAsk(0.1m)));
                bitpay.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("DOGE_BTC"), new BidAsk(0.004m)));
                rateProvider.Providers.Add("bitpay", bitpay);
                var kraken = new MockRateProvider();
                kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("ETH_BTC"), new BidAsk(0.1m)));
                rateProvider.Providers.Add("kraken", kraken);
            }

            // reset test server policies
            var settings = GetService<SettingsRepository>();
            await settings.UpdateSetting(new PoliciesSettings { LockSubscription = false, RequiresUserApproval = false });

            TestLogs.LogInformation("Waiting site is operational...");
            await WaitSiteIsOperational();
            TestLogs.LogInformation("Site is now operational");
        }
        MockRateProvider coinAverageMock;
        private async Task WaitSiteIsOperational()
        {
            _ = HttpClient.GetAsync("/").ConfigureAwait(false);
            using var cts = new CancellationTokenSource(20_000);
            var syncing = WaitIsFullySynched(cts.Token);
            await Task.WhenAll(syncing).ConfigureAwait(false);
            // Opportunistic call to wake up view compilation in debug mode, we don't need to await.
        }

        private async Task WaitIsFullySynched(CancellationToken cancellationToken)
        {
            var o = GetService<IEnumerable<ISyncSummaryProvider>>().ToArray();
            while (!o.All(d => d.AllAvailable()))
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }

        private string FindBTCPayServerDirectory()
        {
            var solutionDirectory = TestUtils.TryGetSolutionDirectoryInfo();
            return Path.Combine(solutionDirectory.FullName, "BTCPayServer");
        }

        public HttpClient HttpClient { get; set; }

        public string HostName
        {
            get;
            internal set;
        }
        public InvoiceRepository InvoiceRepository { get; private set; }
        public StoreRepository StoreRepository { get; private set; }
        public BTCPayNetworkProvider Networks { get; private set; }
        public string IntegratedLightning { get; internal set; }
        public bool InContainer { get; internal set; }

        public T GetService<T>() => _Host.Services.GetRequiredService<T>();

        public IServiceProvider ServiceProvider => _Host.Services;

        public string SSHPassword { get; internal set; }
        public string SSHKeyFile { get; internal set; }
        public string SSHConnection { get; set; }
        public bool NoCSP { get; set; }

        public T GetController<T>(string userId = null, string storeId = null, bool isAdmin = false) where T : Controller
        {
            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("127.0.0.1", Port);
            context.Request.Scheme = "http";
            context.Request.Protocol = "http";
            if (userId != null)
            {
                List<Claim> claims = new List<Claim>();
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
                if (isAdmin)
                    claims.Add(new Claim(ClaimTypes.Role, Roles.ServerAdmin));
                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims.ToArray(), AuthenticationSchemes.Cookie));
            }
            if (storeId != null)
            {
                context.SetStoreData(GetService<StoreRepository>().FindStore(storeId, userId).GetAwaiter().GetResult());
            }
            var scope = (IServiceScopeFactory)_Host.Services.GetService(typeof(IServiceScopeFactory));
            var provider = scope.CreateScope().ServiceProvider;
            context.RequestServices = provider;

            var httpAccessor = provider.GetRequiredService<IHttpContextAccessor>();
            httpAccessor.HttpContext = context;

            var controller = (T)ActivatorUtilities.CreateInstance(provider, typeof(T));

            controller.Url = new UrlHelperMock(new Uri($"http://{HostName}:{Port}/"));
            controller.ControllerContext = new ControllerContext()
            {
                HttpContext = context
            };
            return controller;
        }

        public void Dispose()
        {
            if (_Host != null)
                _Host.Dispose();
        }

        public void ChangeRate(string pair, BidAsk bidAsk)
        {
            var p = CurrencyPair.Parse(pair);
            var index = coinAverageMock.ExchangeRates.FindIndex(o => o.CurrencyPair == p);
            coinAverageMock.ExchangeRates[index] = new PairRate(p, bidAsk);
        }

        public async Task EnableExperimental()
        {
            var r = GetService<SettingsRepository>();
            var p = await r.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            p.Experimental = true;
            await r.UpdateSetting(p);
        }
    }
}
