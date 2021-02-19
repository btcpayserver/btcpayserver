using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Threading;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
using BTCPayServer.PaymentRequest;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payments.PayJoin;
using BTCPayServer.Plugins;
using BTCPayServer.Security;
using BTCPayServer.Security.Bitpay;
using BTCPayServer.Security.GreenField;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Fees;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Shopify;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using BTCPayServer.U2F;
using BundlerMinifier.TagHelpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitpayClient;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using NicolasDorier.RateLimits;
using Serilog;
#if ALTCOINS
using BTCPayServer.Services.Altcoins.Monero;
using BTCPayServer.Services.Altcoins.Ethereum;
#endif
namespace BTCPayServer.Hosting
{
    public static class BTCPayServerServices
    {
        public static IServiceCollection RegisterJsonConverter(this IServiceCollection services, Func<BTCPayNetwork, JsonConverter> create)
        {
            services.AddSingleton<IJsonConverterRegistration, JsonConverterRegistration>((s) => new JsonConverterRegistration(create));
            return services;
        }
        public static IServiceCollection AddBTCPayServer(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<MvcNewtonsoftJsonOptions>(o => o.GetRequiredService<IOptions<MvcNewtonsoftJsonOptions>>().Value);
            services.AddDbContext<ApplicationDbContext>((provider, o) =>
            {
                var factory = provider.GetRequiredService<ApplicationDbContextFactory>();
                factory.ConfigureBuilder(o);
            });
            services.AddHttpClient();
            services.AddHttpClient(nameof(ExplorerClientProvider), httpClient =>
            {
                httpClient.Timeout = Timeout.InfiniteTimeSpan;
            });

            services.AddSingleton<BTCPayNetworkJsonSerializerSettings>();
            services.RegisterJsonConverter(n => new ClaimDestinationJsonConverter(n));

            services.AddPayJoinServices();
#if ALTCOINS
            services.AddMoneroLike();
            services.AddEthereumLike();
#endif
            services.TryAddSingleton<SettingsRepository>();
            services.TryAddSingleton<ISettingsRepository>(provider => provider.GetService<SettingsRepository>());
            services.TryAddSingleton<LabelFactory>();
            services.TryAddSingleton<TorServices>();
            services.TryAddSingleton<SocketFactory>();
            services.TryAddSingleton<LightningClientFactoryService>();
            services.TryAddSingleton<InvoicePaymentNotification>();
            services.TryAddSingleton<BTCPayServerOptions>(o =>
                o.GetRequiredService<IOptions<BTCPayServerOptions>>().Value);
            // Don't move this StartupTask, we depend on it being right here
            services.AddStartupTask<MigrationStartupTask>();
            // 
            services.AddStartupTask<BlockExplorerLinkStartupTask>();
            services.TryAddSingleton<InvoiceRepository>(o =>
            {
                var dbContext = o.GetRequiredService<ApplicationDbContextFactory>();
                return new InvoiceRepository(dbContext, o.GetRequiredService<BTCPayNetworkProvider>(), o.GetService<EventAggregator>());
            });
            services.AddSingleton<BTCPayServerEnvironment>();
            services.TryAddSingleton<TokenRepository>();
            services.TryAddSingleton<WalletRepository>();
            services.TryAddSingleton<EventAggregator>();
            services.TryAddSingleton<PaymentRequestService>();
            services.TryAddSingleton<U2FService>();
            services.AddSingleton<ApplicationDbContextFactory>();
            services.AddOptions<BTCPayServerOptions>().Configure(
                (options) =>
                {
                    options.LoadArgs(configuration);
                });
            services.AddOptions<DataDirectories>().Configure(
                (options) =>
                {
                    options.Configure(configuration);
                });
            services.AddOptions<DatabaseOptions>().Configure<IOptions<DataDirectories>>(
                (options, datadirs) =>
                {
                    var postgresConnectionString = configuration["postgres"];
                    var mySQLConnectionString = configuration["mysql"];
                    var sqliteFileName = configuration["sqlitefile"];

                    if (!string.IsNullOrEmpty(postgresConnectionString))
                    {
                        options.DatabaseType = DatabaseType.Postgres;
                        options.ConnectionString = postgresConnectionString;
                    }
                    else if (!string.IsNullOrEmpty(mySQLConnectionString))
                    {
                        options.DatabaseType = DatabaseType.MySQL;
                        options.ConnectionString = mySQLConnectionString;
                    }
                    else if (!string.IsNullOrEmpty(sqliteFileName))
                    {
                        var connStr = "Data Source=" + (Path.IsPathRooted(sqliteFileName)
                            ? sqliteFileName
                            : Path.Combine(datadirs.Value.DataDir, sqliteFileName));

                        options.DatabaseType = DatabaseType.Sqlite;
                        options.ConnectionString = connStr;
                    }
                    else
                    {
                        throw new InvalidOperationException("No database option was configured.");
                    }
                });
            services.AddOptions<NBXplorerOptions>().Configure<BTCPayNetworkProvider>(
                (options, btcPayNetworkProvider) =>
                {
                    foreach (BTCPayNetwork btcPayNetwork in btcPayNetworkProvider.GetAll().OfType<BTCPayNetwork>())
                    {
                        NBXplorerConnectionSetting setting =
                            new NBXplorerConnectionSetting
                            {
                                CryptoCode = btcPayNetwork.CryptoCode,
                                ExplorerUri = configuration.GetOrDefault<Uri>(
                                    $"{btcPayNetwork.CryptoCode}.explorer.url",
                                    btcPayNetwork.NBXplorerNetwork.DefaultSettings.DefaultUrl),
                                CookieFile = configuration.GetOrDefault<string>(
                                    $"{btcPayNetwork.CryptoCode}.explorer.cookiefile",
                                    btcPayNetwork.NBXplorerNetwork.DefaultSettings.DefaultCookieFile)
                            };
                        options.NBXplorerConnectionSettings.Add(setting);
                    }
                });
            services.AddOptions<LightningNetworkOptions>().Configure<BTCPayNetworkProvider>(
                (options, btcPayNetworkProvider) =>
                {
                    foreach (var net in btcPayNetworkProvider.GetAll().OfType<BTCPayNetwork>())
                    {
                        var lightning = configuration.GetOrDefault<string>($"{net.CryptoCode}.lightning", string.Empty);
                        if (lightning.Length != 0)
                        {
                            if (!LightningConnectionString.TryParse(lightning, true, out var connectionString,
                                out var error))
                            {
                                Logs.Configuration.LogWarning($"Invalid setting {net.CryptoCode}.lightning, " +
                                                              Environment.NewLine +
                                                              $"If you have a c-lightning server use: 'type=clightning;server=/root/.lightning/lightning-rpc', " +
                                                              Environment.NewLine +
                                                              $"If you have a lightning charge server: 'type=charge;server=https://charge.example.com;api-token=yourapitoken'" +
                                                              Environment.NewLine +
                                                              $"If you have a lnd server: 'type=lnd-rest;server=https://lnd:lnd@lnd.example.com;macaroon=abf239...;certthumbprint=2abdf302...'" +
                                                              Environment.NewLine +
                                                              $"              lnd server: 'type=lnd-rest;server=https://lnd:lnd@lnd.example.com;macaroonfilepath=/root/.lnd/admin.macaroon;certthumbprint=2abdf302...'" +
                                                              Environment.NewLine +
                                                              $"If you have an eclair server: 'type=eclair;server=http://eclair.com:4570;password=eclairpassword;bitcoin-host=bitcoind:37393;bitcoin-auth=bitcoinrpcuser:bitcoinrpcpassword" +
                                                              Environment.NewLine +
                                                              $"               eclair server: 'type=eclair;server=http://eclair.com:4570;password=eclairpassword;bitcoin-host=bitcoind:37393" +
                                                              Environment.NewLine +
                                                              $"Error: {error}" + Environment.NewLine +
                                                              "This service will not be exposed through BTCPay Server");
                            }
                            else
                            {
                                if (connectionString.IsLegacy)
                                {
                                    Logs.Configuration.LogWarning(
                                        $"Setting {net.CryptoCode}.lightning is a deprecated format, it will work now, but please replace it for future versions with '{connectionString.ToString()}'");
                                }
                                options.InternalLightningByCryptoCode.Add(net.CryptoCode, connectionString);
                            }
                        }
                    }
                });
            services.AddOptions<ExternalServicesOptions>().Configure<BTCPayNetworkProvider>(
                (options, btcPayNetworkProvider) =>
                {
                    foreach (var net in btcPayNetworkProvider.GetAll().OfType<BTCPayNetwork>())
                    {
                        options.ExternalServices.Load(net.CryptoCode, configuration);
                    }

                    options.ExternalServices.LoadNonCryptoServices(configuration);

                    var services = configuration.GetOrDefault<string>("externalservices", null);
                    if (services != null)
                    {
                        foreach (var service in services.Split(new[] {';', ','}, StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => (p, SeparatorIndex: p.IndexOf(':', StringComparison.OrdinalIgnoreCase)))
                            .Where(p => p.SeparatorIndex != -1)
                            .Select(p => (Name: p.p.Substring(0, p.SeparatorIndex),
                                Link: p.p.Substring(p.SeparatorIndex + 1))))
                        {
                            if (Uri.TryCreate(service.Link, UriKind.RelativeOrAbsolute, out var uri))
                                options.OtherExternalServices.AddOrReplace(service.Name, uri);
                        }
                    }
                });
            services.TryAddSingleton(o => configuration.ConfigureNetworkProvider());

            services.TryAddSingleton<AppService>();
            services.AddSingleton<PluginService>();
            services.AddSingleton<IPluginHookService, PluginHookService>();
            services.TryAddTransient<Safe>();
            services.TryAddSingleton<Ganss.XSS.HtmlSanitizer>(o =>
            {

                var htmlSanitizer = new Ganss.XSS.HtmlSanitizer();


                htmlSanitizer.RemovingAtRule += (sender, args) =>
                {
                };
                htmlSanitizer.RemovingTag += (sender, args) =>
                {
                    if (args.Tag.TagName.Equals("img", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (!args.Tag.ClassList.Contains("img-fluid"))
                        {
                            args.Tag.ClassList.Add("img-fluid");
                        }

                        args.Cancel = true;
                    }
                };

                htmlSanitizer.RemovingAttribute += (sender, args) =>
                {
                    if (args.Tag.TagName.Equals("img", StringComparison.InvariantCultureIgnoreCase) &&
                        args.Attribute.Name.Equals("src", StringComparison.InvariantCultureIgnoreCase) &&
                        args.Reason == Ganss.XSS.RemoveReason.NotAllowedUrlValue)
                    {
                        args.Cancel = true;
                    }
                };
                htmlSanitizer.RemovingStyle += (sender, args) => { args.Cancel = true; };
                htmlSanitizer.AllowedAttributes.Add("class");
                htmlSanitizer.AllowedTags.Add("iframe");
                htmlSanitizer.AllowedTags.Add("style");
                htmlSanitizer.AllowedTags.Remove("img");
                htmlSanitizer.AllowedAttributes.Add("webkitallowfullscreen");
                htmlSanitizer.AllowedAttributes.Add("allowfullscreen");
                return htmlSanitizer;
            });

            services.TryAddSingleton<LightningConfigurationProvider>();
            services.TryAddSingleton<LanguageService>();
            services.TryAddSingleton<NBXplorerDashboard>();
            services.AddSingleton<ISyncSummaryProvider, NBXSyncSummaryProvider>();
            services.TryAddSingleton<StoreRepository>();
            services.TryAddSingleton<PaymentRequestRepository>();
            services.TryAddSingleton<BTCPayWalletProvider>();
            services.TryAddSingleton<WalletReceiveService>();
            services.AddSingleton<IHostedService>( provider => provider.GetService<WalletReceiveService>());
            services.TryAddSingleton<CurrencyNameTable>(CurrencyNameTable.Instance);
            services.TryAddSingleton<IFeeProviderFactory>(o => new NBXplorerFeeProviderFactory(o.GetRequiredService<ExplorerClientProvider>())
            {
                Fallback = new FeeRate(100L, 1)
            });

            services.AddSingleton<CssThemeManager>();
            services.Configure<MvcOptions>((o) =>
            {
                o.Filters.Add(new ContentSecurityPolicyCssThemeManager());
                o.ModelMetadataDetailsProviders.Add(new SuppressChildValidationMetadataProvider(typeof(WalletId)));
                o.ModelMetadataDetailsProviders.Add(new SuppressChildValidationMetadataProvider(typeof(DerivationStrategyBase)));
            });
            services.AddSingleton<IHostedService, CssThemeManagerHostedService>();

            services.AddSingleton<HostedServices.CheckConfigurationHostedService>();
            services.AddSingleton<IHostedService, HostedServices.CheckConfigurationHostedService>(o => o.GetRequiredService<CheckConfigurationHostedService>());
            services.AddSingleton<HostedServices.WebhookNotificationManager>();
            services.AddSingleton<IHostedService, WebhookNotificationManager>(o => o.GetRequiredService<WebhookNotificationManager>());
            services.AddHttpClient(WebhookNotificationManager.OnionNamedClient)
                .ConfigureHttpClient(h => h.DefaultRequestHeaders.ConnectionClose = true)
                .ConfigurePrimaryHttpMessageHandler<Socks5HttpClientHandler>();
            
            services.AddSingleton<HostedServices.PullPaymentHostedService>();
            services.AddSingleton<IHostedService, HostedServices.PullPaymentHostedService>(o => o.GetRequiredService<PullPaymentHostedService>());

            services.AddSingleton<BitcoinLikePaymentHandler>();
            services.AddSingleton<IPaymentMethodHandler>(provider => provider.GetService<BitcoinLikePaymentHandler>());
            services.AddSingleton<IHostedService, NBXplorerListener>();

            services.AddSingleton<LightningLikePaymentHandler>();
            services.AddSingleton<IPaymentMethodHandler>(provider => provider.GetService<LightningLikePaymentHandler>());
            services.AddSingleton<IHostedService, LightningListener>();

            services.AddSingleton<PaymentMethodHandlerDictionary>();

            services.AddSingleton<NotificationManager>();
            services.AddScoped<NotificationSender>();

            services.AddSingleton<IHostedService, NBXplorerWaiters>();
            services.AddSingleton<IHostedService, InvoiceEventSaverService>();
            services.AddSingleton<IHostedService, InvoiceNotificationManager>();
            services.AddSingleton<IHostedService, InvoiceWatcher>();
            services.AddSingleton<IHostedService, RatesHostedService>();
            services.AddSingleton<IHostedService, BackgroundJobSchedulerHostedService>();
            services.AddSingleton<IHostedService, AppHubStreamer>();
            services.AddSingleton<IHostedService, AppInventoryUpdaterHostedService>();
            services.AddSingleton<IHostedService, TransactionLabelMarkerHostedService>();
            services.AddSingleton<IHostedService, UserEventHostedService>();
            services.AddSingleton<IHostedService, DynamicDnsHostedService>();
            services.AddSingleton<IHostedService, TorServicesHostedService>();
            services.AddSingleton<IHostedService, PaymentRequestStreamer>();
            services.AddSingleton<IBackgroundJobClient, BackgroundJobClient>();
            services.AddScoped<IAuthorizationHandler, CookieAuthorizationHandler>();
            services.AddScoped<IAuthorizationHandler, BitpayAuthorizationHandler>();

            services.AddSingleton<IVersionFetcher, GithubVersionFetcher>();
            services.AddSingleton<IHostedService, NewVersionCheckerHostedService>();
            services.AddSingleton<INotificationHandler, NewVersionNotification.Handler>();

            services.AddSingleton<INotificationHandler, InvoiceEventNotification.Handler>();
            services.AddSingleton<INotificationHandler, PayoutNotification.Handler>();

            services.AddSingleton<IHostedService, DbMigrationsHostedService>();

            services.AddShopify();
#if DEBUG
            services.AddSingleton<INotificationHandler, JunkNotification.Handler>();
#endif    
            services.TryAddSingleton<ExplorerClientProvider>();
            services.TryAddSingleton<Bitpay>(o =>
            {
                if (o.GetRequiredService<BTCPayServerOptions>().NetworkType == ChainName.Mainnet)
                    return new Bitpay(new Key(), new Uri("https://bitpay.com/"));
                else
                    return new Bitpay(new Key(), new Uri("https://test.bitpay.com/"));
            });
            services.TryAddSingleton<RateProviderFactory>();
            services.TryAddSingleton<RateFetcher>();

            services.TryAddScoped<IHttpContextAccessor, HttpContextAccessor>();
            services.AddTransient<AccessTokenController>();
            services.AddTransient<InvoiceController>();
            services.AddTransient<AppsPublicController>();
            services.AddTransient<PaymentRequestController>();
            // Add application services.
            services.AddSingleton<EmailSenderFactory>();

            services.AddAPIKeyAuthentication();
            services.AddBtcPayServerAuthenticationSchemes();
            services.AddAuthorization(o => o.AddBTCPayPolicies());
            // bundling
            services.AddSingleton<IBundleProvider, ResourceBundleProvider>();
            services.AddTransient<BundleOptions>(provider =>
            {
                var opts = provider.GetRequiredService<BTCPayServerOptions>();
                var bundle = new BundleOptions();
                bundle.UseBundles = opts.BundleJsCss;
                bundle.AppendVersion = true;
                return bundle;
            });

            services.AddCors(options =>
            {
                options.AddPolicy(CorsPolicies.All, p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
            });
            services.AddSingleton(provider =>
            {
                var btcPayEnv = provider.GetService<BTCPayServerEnvironment>();
                var rateLimits = new RateLimitService();
                if (btcPayEnv.IsDeveloping)
                {
                    rateLimits.SetZone($"zone={ZoneLimits.Login} rate=1000r/min burst=100 nodelay");
                    rateLimits.SetZone($"zone={ZoneLimits.Register} rate=1000r/min burst=100 nodelay");
                    rateLimits.SetZone($"zone={ZoneLimits.PayJoin} rate=1000r/min burst=100 nodelay");
                    rateLimits.SetZone($"zone={ZoneLimits.Shopify} rate=1000r/min burst=100 nodelay");
                }
                else
                {
                    rateLimits.SetZone($"zone={ZoneLimits.Login} rate=5r/min burst=3 nodelay");
                    rateLimits.SetZone($"zone={ZoneLimits.Register} rate=2r/min burst=2 nodelay");
                    rateLimits.SetZone($"zone={ZoneLimits.PayJoin} rate=5r/min burst=3 nodelay");
                    rateLimits.SetZone($"zone={ZoneLimits.Shopify} rate=20r/min burst=3 nodelay");
                }
                return rateLimits;
            });
            services.AddLogging(logBuilder =>
            {
                var debugLogFile = BTCPayServerOptions.GetDebugLog(configuration);
                if (!string.IsNullOrEmpty(debugLogFile))
                {
                    Serilog.Log.Logger = new LoggerConfiguration()
                        .Enrich.FromLogContext()
                        .MinimumLevel.Is(BTCPayServerOptions.GetDebugLogLevel(configuration))
                        .WriteTo.File(debugLogFile, rollingInterval: RollingInterval.Day, fileSizeLimitBytes: MAX_DEBUG_LOG_FILE_SIZE, rollOnFileSizeLimit: true, retainedFileCountLimit: 1)
                        .CreateLogger();
                    logBuilder.AddProvider(new Serilog.Extensions.Logging.SerilogLoggerProvider(Log.Logger));
                }
            });

            services.AddSingleton<IObjectModelValidator, SkippableObjectValidatorProvider>();
            services.SkipModelValidation<RootedKeyPath>();
            return services;
        }

        public static void SkipModelValidation<T>(this IServiceCollection services)
        {
            services.AddSingleton<SkippableObjectValidatorProvider.ISkipValidation, SkippableObjectValidatorProvider.SkipValidationType<T>>();
        }
        private const long MAX_DEBUG_LOG_FILE_SIZE = 2000000; // If debug log is in use roll it every N MB.
        private static void AddBtcPayServerAuthenticationSchemes(this IServiceCollection services)
        {
            services.AddAuthentication()
                .AddCookie()
                .AddBitpayAuthentication()
                .AddAPIKeyAuthentication();
        }

        public static IApplicationBuilder UsePayServer(this IApplicationBuilder app)
        {
            app.UseMiddleware<BTCPayMiddleware>();
            return app;
        }
        public static IApplicationBuilder UseHeadersOverride(this IApplicationBuilder app)
        {
            app.UseMiddleware<HeadersOverrideMiddleware>();
            return app;
        }
    }
}
