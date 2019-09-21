﻿using BTCPayServer.Configuration;
using System.Linq;
using BTCPayServer.HostedServices;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Rating;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Tests.Logging;
using BTCPayServer.Tests.Mocks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Threading;
using AspNet.Security.OpenIdConnect.Primitives;
using Xunit;
using BTCPayServer.Services;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Threading.Tasks;

namespace BTCPayServer.Tests
{
    public enum TestDatabases
    {
        Postgres,
        MySQL,
    }

    public class BTCPayServerTester : IDisposable
    {
        private string _Directory;

        public BTCPayServerTester(string scope)
        {
            this._Directory = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        public Uri NBXplorerUri
        {
            get; set;
        }

        public Uri LTCNBXplorerUri { get; set; }

        public Uri ServerUri
        {
            get;
            set;
        }

        public string MySQL
        {
            get; set;
        }

        public string Postgres
        {
            get; set;
        }

        IWebHost _Host;
        public int Port
        {
            get; set;
        }

        public TestDatabases TestDatabase
        {
            get; set;
        }

        public bool MockRates { get; set; } = true;

        public void Start()
        {
            if (!Directory.Exists(_Directory))
                Directory.CreateDirectory(_Directory);
            string chain = NBXplorerDefaultSettings.GetFolderName(NetworkType.Regtest);
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
            config.AppendLine($"chains=btc,ltc");

            config.AppendLine($"btc.explorer.url={NBXplorerUri.AbsoluteUri}");
            config.AppendLine($"btc.explorer.cookiefile=0");
            config.AppendLine("allow-admin-registration=1");
            config.AppendLine($"ltc.explorer.url={LTCNBXplorerUri.AbsoluteUri}");
            config.AppendLine($"ltc.explorer.cookiefile=0");
            config.AppendLine($"btc.lightning={IntegratedLightning.AbsoluteUri}");
            if (!string.IsNullOrEmpty(SSHPassword) && string.IsNullOrEmpty(SSHKeyFile))
                config.AppendLine($"sshpassword={SSHPassword}");
            if (!string.IsNullOrEmpty(SSHKeyFile))
                config.AppendLine($"sshkeyfile={SSHKeyFile}");
            if (!string.IsNullOrEmpty(SSHConnection))
                config.AppendLine($"sshconnection={SSHConnection}");

            if (TestDatabase == TestDatabases.MySQL && !String.IsNullOrEmpty(MySQL))
                config.AppendLine($"mysql=" + MySQL);
            else if (!String.IsNullOrEmpty(Postgres))
                config.AppendLine($"postgres=" + Postgres);
            var confPath = Path.Combine(chainDirectory, "settings.config");
            File.WriteAllText(confPath, config.ToString());

            ServerUri = new Uri("http://" + HostName + ":" + Port + "/");
            HttpClient = new HttpClient();
            HttpClient.BaseAddress = ServerUri;
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            var conf = new DefaultConfiguration() { Logger = Logs.LogProvider.CreateLogger("Console") }.CreateConfiguration(new[] { "--datadir", _Directory, "--conf", confPath, "--disable-registration", "false" });
            _Host = new WebHostBuilder()
                    .UseConfiguration(conf)
                    .UseContentRoot(FindBTCPayServerDirectory())
                    .ConfigureServices(s =>
                    {
                        s.AddLogging(l =>
                        {
                            l.AddFilter("System.Net.Http.HttpClient", LogLevel.Critical);
                            l.SetMinimumLevel(LogLevel.Information)
                            .AddFilter("Microsoft", LogLevel.Error)
                            .AddFilter("Hangfire", LogLevel.Error)
                            .AddProvider(Logs.LogProvider);
                        });
                    })
                    .UseKestrel()
                    .UseStartup<Startup>()
                    .Build();
            _Host.StartWithTasksAsync().GetAwaiter().GetResult();

            var urls = _Host.ServerFeatures.Get<IServerAddressesFeature>().Addresses;
            foreach (var url in urls)
            {
                Logs.Tester.LogInformation("Listening on " + url);
            }
            Logs.Tester.LogInformation("Server URI " + ServerUri);

            InvoiceRepository = (InvoiceRepository)_Host.Services.GetService(typeof(InvoiceRepository));
            StoreRepository = (StoreRepository)_Host.Services.GetService(typeof(StoreRepository));
            Networks = (BTCPayNetworkProvider)_Host.Services.GetService(typeof(BTCPayNetworkProvider));

            if (MockRates)
            {
                var rateProvider = (RateProviderFactory)_Host.Services.GetService(typeof(RateProviderFactory));
                rateProvider.Providers.Clear();

                var coinAverageMock = new MockRateProvider();
                coinAverageMock.ExchangeRates.Add(new Rating.ExchangeRate()
                {
                    Exchange = "coinaverage",
                    CurrencyPair = CurrencyPair.Parse("BTC_USD"),
                    BidAsk = new BidAsk(5000m)
                });
                coinAverageMock.ExchangeRates.Add(new Rating.ExchangeRate()
                {
                    Exchange = "coinaverage",
                    CurrencyPair = CurrencyPair.Parse("BTC_CAD"),
                    BidAsk = new BidAsk(4500m)
                });
                coinAverageMock.ExchangeRates.Add(new Rating.ExchangeRate()
                {
                    Exchange = "coinaverage",
                    CurrencyPair = CurrencyPair.Parse("LTC_BTC"),
                    BidAsk = new BidAsk(0.001m)
                });
                coinAverageMock.ExchangeRates.Add(new Rating.ExchangeRate()
                {
                    Exchange = "coinaverage",
                    CurrencyPair = CurrencyPair.Parse("LTC_USD"),
                    BidAsk = new BidAsk(500m)
                });
                rateProvider.Providers.Add("coinaverage", coinAverageMock);

                var bitflyerMock = new MockRateProvider();
                bitflyerMock.ExchangeRates.Add(new Rating.ExchangeRate()
                {
                    Exchange = "bitflyer",
                    CurrencyPair = CurrencyPair.Parse("BTC_JPY"),
                    BidAsk = new BidAsk(700000m)
                });
                rateProvider.Providers.Add("bitflyer", bitflyerMock);

                var quadrigacx = new MockRateProvider();
                quadrigacx.ExchangeRates.Add(new Rating.ExchangeRate()
                {
                    Exchange = "quadrigacx",
                    CurrencyPair = CurrencyPair.Parse("BTC_CAD"),
                    BidAsk = new BidAsk(6000m)
                });
                rateProvider.Providers.Add("quadrigacx", quadrigacx);

                var bittrex = new MockRateProvider();
                bittrex.ExchangeRates.Add(new Rating.ExchangeRate()
                {
                    Exchange = "bittrex",
                    CurrencyPair = CurrencyPair.Parse("DOGE_BTC"),
                    BidAsk = new BidAsk(0.004m)
                });
                rateProvider.Providers.Add("bittrex", bittrex);
            }

            

            WaitSiteIsOperational().GetAwaiter().GetResult();
        }

        private async Task WaitSiteIsOperational()
        {
            using (var cts = new CancellationTokenSource(10_000))
            {
                var synching = WaitIsFullySynched(cts.Token);
                var accessingHomepage = WaitCanAccessHomepage(cts.Token);
                await Task.WhenAll(synching, accessingHomepage).ConfigureAwait(false);
            }
        }

        private async Task WaitCanAccessHomepage(CancellationToken cancellationToken)
        {
            while (true)
            {
                var resp = await HttpClient.GetAsync("/", cancellationToken).ConfigureAwait(false);
                if (resp.StatusCode == HttpStatusCode.OK)
                    break;
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task WaitIsFullySynched(CancellationToken cancellationToken)
        {
            var dashBoard = GetService<NBXplorerDashboard>();
            while (!dashBoard.IsFullySynched())
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }

        private string FindBTCPayServerDirectory()
        {
            var solutionDirectory = LanguageService.TryGetSolutionDirectoryInfo(Directory.GetCurrentDirectory());
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
        public Uri IntegratedLightning { get; internal set; }
        public bool InContainer { get; internal set; }

        public T GetService<T>()
        {
            return _Host.Services.GetRequiredService<T>();
        }

        public IServiceProvider ServiceProvider => _Host.Services;

        public string SSHPassword { get; internal set; }
        public string SSHKeyFile { get; internal set; }
        public string SSHConnection { get; set; }
        public T GetController<T>(string userId = null, string storeId = null, Claim[] additionalClaims = null) where T : Controller
        {
            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("127.0.0.1", Port);
            context.Request.Scheme = "http";
            context.Request.Protocol = "http";
            if (userId != null)
            {
                List<Claim> claims = new List<Claim>();
                claims.Add(new Claim(OpenIdConnectConstants.Claims.Subject, userId));
                if (additionalClaims != null)
                    claims.AddRange(additionalClaims);
                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims.ToArray(), Policies.CookieAuthentication));
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
    }
}
