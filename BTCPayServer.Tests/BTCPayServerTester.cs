﻿using BTCPayServer.Configuration;
using BTCPayServer.Hosting;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
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
using Xunit;

namespace BTCPayServer.Tests
{
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

        public string Postgres
        {
            get; set;
        }

        IWebHost _Host;
        public int Port
        {
            get; set;
        }

        public void Start()
        {
            if (!Directory.Exists(_Directory))
                Directory.CreateDirectory(_Directory);
            string chain = ChainType.Regtest.ToNetwork().Name;
            string chainDirectory = Path.Combine(_Directory, chain);
            if (!Directory.Exists(chainDirectory))
                Directory.CreateDirectory(chainDirectory);


            StringBuilder config = new StringBuilder();
            config.AppendLine($"{chain.ToLowerInvariant()}=1");
            config.AppendLine($"port={Port}");
            config.AppendLine($"chains=btc,ltc");

            config.AppendLine($"btc.explorer.url={NBXplorerUri.AbsoluteUri}");
            config.AppendLine($"btc.explorer.cookiefile=0");

            config.AppendLine($"ltc.explorer.url={LTCNBXplorerUri.AbsoluteUri}");
            config.AppendLine($"ltc.explorer.cookiefile=0");

            config.AppendLine($"internallightningnode={IntegratedLightning.AbsoluteUri}");

            if (Postgres != null)
                config.AppendLine($"postgres=" + Postgres);
            var confPath = Path.Combine(chainDirectory, "settings.config");
            File.WriteAllText(confPath, config.ToString());

            ServerUri = new Uri("http://" + HostName + ":" + Port + "/");

            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            var conf = new DefaultConfiguration() { Logger = Logs.LogProvider.CreateLogger("Console") }.CreateConfiguration(new[] { "--datadir", _Directory, "--conf", confPath });
            _Host = new WebHostBuilder()
                    .UseConfiguration(conf)
                    .ConfigureServices(s =>
                    {
                        var mockRates = new MockRateProviderFactory();
                        var btc = new MockRateProvider("BTC", new Rate("USD", 5000m));
                        var ltc = new MockRateProvider("LTC", new Rate("USD", 500m));
                        mockRates.AddMock(btc);
                        mockRates.AddMock(ltc);
                        s.AddSingleton<IRateProviderFactory>(mockRates);
                        s.AddLogging(l =>
                        {
                            l.SetMinimumLevel(LogLevel.Information)
                            .AddFilter("Microsoft", LogLevel.Error)
                            .AddFilter("Hangfire", LogLevel.Error)
                            .AddProvider(Logs.LogProvider);
                        });
                    })
                    .UseKestrel()
                    .UseStartup<Startup>()
                    .Build();
            _Host.Start();
            InvoiceRepository = (InvoiceRepository)_Host.Services.GetService(typeof(InvoiceRepository));
        }
        
        public string HostName
        {
            get;
            internal set;
        }
        public InvoiceRepository InvoiceRepository { get; private set; }
        public Uri IntegratedLightning { get; internal set; }

        public T GetService<T>()
        {
            return _Host.Services.GetRequiredService<T>();
        }

        public T GetController<T>(string userId = null) where T : Controller
        {
            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("127.0.0.1");
            context.Request.Scheme = "http";
            context.Request.Protocol = "http";
            if (userId != null)
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }));
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
