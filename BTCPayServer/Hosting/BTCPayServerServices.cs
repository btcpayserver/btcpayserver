using BTCPayServer.Configuration;
using BTCPayServer.Services.Altcoins.Monero;
using Microsoft.Extensions.Logging;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Http;
using NBitpayClient;
using NBitcoin;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Fees;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using BTCPayServer.Controllers;
using BTCPayServer.Services.Mails;
using System.Threading;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Logging;
using BTCPayServer.HostedServices;
using BTCPayServer.PaymentRequest;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Security;
using BTCPayServer.Services.PaymentRequests;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBXplorer.DerivationStrategy;
using NicolasDorier.RateLimits;
using Npgsql;
using BTCPayServer.Services.Apps;
using BTCPayServer.U2F;
using BundlerMinifier.TagHelpers;
using OpenIddict.EntityFrameworkCore.Models;

using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using BTCPayServer.Security.Bitpay;
using Serilog;

namespace BTCPayServer.Hosting
{
    public static class BTCPayServerServices
    {
        public static IServiceCollection AddBTCPayServer(this IServiceCollection services, IConfiguration configuration)
        {
			services.AddSingleton<MvcNewtonsoftJsonOptions>(o =>  o.GetRequiredService<IOptions<MvcNewtonsoftJsonOptions>>().Value);
            services.AddDbContext<ApplicationDbContext>((provider, o) =>
            {
                var factory = provider.GetRequiredService<ApplicationDbContextFactory>();
                factory.ConfigureBuilder(o);
                o.UseOpenIddict<BTCPayOpenIdClient, BTCPayOpenIdAuthorization, OpenIddictScope<string>, BTCPayOpenIdToken, string>();
            });
            services.AddHttpClient();
            services.AddHttpClient(nameof(ExplorerClientProvider), httpClient =>
            {
                httpClient.Timeout = Timeout.InfiniteTimeSpan;
            });
            services.AddMoneroLike();
            services.TryAddSingleton<SettingsRepository>();
            services.TryAddSingleton<TorServices>();
            services.TryAddSingleton<SocketFactory>();
            services.TryAddSingleton<LightningClientFactoryService>();
            services.TryAddSingleton<InvoicePaymentNotification>();
            services.TryAddSingleton<BTCPayServerOptions>(o =>
                o.GetRequiredService<IOptions<BTCPayServerOptions>>().Value);
            services.AddStartupTask<MigrationStartupTask>();
            services.TryAddSingleton<InvoiceRepository>(o =>
            {
                var opts = o.GetRequiredService<BTCPayServerOptions>();
                var dbContext = o.GetRequiredService<ApplicationDbContextFactory>();
                var dbpath = Path.Combine(opts.DataDir, "InvoiceDB");
                if (!Directory.Exists(dbpath))
                    Directory.CreateDirectory(dbpath);
                return new InvoiceRepository(dbContext, dbpath, o.GetRequiredService<BTCPayNetworkProvider>());
            });
            services.AddSingleton<BTCPayServerEnvironment>();
            services.TryAddSingleton<TokenRepository>();
            services.TryAddSingleton<WalletRepository>();
            services.TryAddSingleton<EventAggregator>();
            services.TryAddSingleton<PaymentRequestService>();
            services.TryAddSingleton<U2FService>();
            services.TryAddSingleton<ApplicationDbContextFactory>(o => 
            {
                var opts = o.GetRequiredService<BTCPayServerOptions>();
                ApplicationDbContextFactory dbContext = null;
                if (!String.IsNullOrEmpty(opts.PostgresConnectionString))
                {
                    Logs.Configuration.LogInformation($"Postgres DB used ({opts.PostgresConnectionString})");
                    dbContext = new ApplicationDbContextFactory(DatabaseType.Postgres, opts.PostgresConnectionString);
                }
                else if(!String.IsNullOrEmpty(opts.MySQLConnectionString))
                {
                    Logs.Configuration.LogInformation($"MySQL DB used ({opts.MySQLConnectionString})");
                    Logs.Configuration.LogWarning("MySQL is not widely tested and should be considered experimental, we advise you to use postgres instead.");
                    dbContext = new ApplicationDbContextFactory(DatabaseType.MySQL, opts.MySQLConnectionString);
                }
                else
                {
                    var connStr = "Data Source=" + Path.Combine(opts.DataDir, "sqllite.db");
                    Logs.Configuration.LogInformation($"SQLite DB used ({connStr})");
                    Logs.Configuration.LogWarning("MySQL is not widely tested and should be considered experimental, we advise you to use postgres instead.");
                    dbContext = new ApplicationDbContextFactory(DatabaseType.Sqlite, connStr);
                }
                 
                return dbContext;
            });

            services.TryAddSingleton<BTCPayNetworkProvider>(o => 
            {
                var opts = o.GetRequiredService<BTCPayServerOptions>();
                return opts.NetworkProvider;
            });

            services.TryAddSingleton<AppService>();
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
                htmlSanitizer.AllowedTags.Remove("img");
                htmlSanitizer.AllowedAttributes.Add("webkitallowfullscreen");
                htmlSanitizer.AllowedAttributes.Add("allowfullscreen");
                return htmlSanitizer;
            });

            services.TryAddSingleton<LightningConfigurationProvider>();
            services.TryAddSingleton<LanguageService>();
            services.TryAddSingleton<NBXplorerDashboard>();
            services.TryAddSingleton<StoreRepository>();
            services.TryAddSingleton<PaymentRequestRepository>();
            services.TryAddSingleton<BTCPayWalletProvider>();
            services.TryAddSingleton<WalletReceiveStateService>();
            services.TryAddSingleton<CurrencyNameTable>();
            services.TryAddSingleton<IFeeProviderFactory>(o => new NBXplorerFeeProviderFactory(o.GetRequiredService<ExplorerClientProvider>())
            {
                Fallback = new FeeRate(100L, 1)
            });

            services.AddSingleton<CssThemeManager>();
            services.Configure<MvcOptions>((o) => {
                o.Filters.Add(new ContentSecurityPolicyCssThemeManager());
                o.ModelMetadataDetailsProviders.Add(new SuppressChildValidationMetadataProvider(typeof(WalletId)));
                o.ModelMetadataDetailsProviders.Add(new SuppressChildValidationMetadataProvider(typeof(DerivationStrategyBase)));
            });
            services.AddSingleton<IHostedService, CssThemeManagerHostedService>();

            services.AddSingleton<HostedServices.CheckConfigurationHostedService>();
            services.AddSingleton<IHostedService, HostedServices.CheckConfigurationHostedService>(o => o.GetRequiredService<CheckConfigurationHostedService>());
            
            services.AddSingleton<BitcoinLikePaymentHandler>();
            services.AddSingleton<IPaymentMethodHandler>(provider => provider.GetService<BitcoinLikePaymentHandler>());
            services.AddSingleton<IHostedService, NBXplorerListener>();

            services.AddSingleton<LightningLikePaymentHandler>();
            services.AddSingleton<IPaymentMethodHandler>(provider => provider.GetService<LightningLikePaymentHandler>());
            services.AddSingleton<IHostedService, LightningListener>();

            services.AddSingleton<PaymentMethodHandlerDictionary>();

            services.AddSingleton<ChangellyClientProvider>();

            services.AddSingleton<IHostedService, NBXplorerWaiters>();
            services.AddSingleton<IHostedService, InvoiceNotificationManager>();
            services.AddSingleton<IHostedService, InvoiceWatcher>();
            services.AddSingleton<IHostedService, RatesHostedService>();
            services.AddSingleton<IHostedService, BackgroundJobSchedulerHostedService>();
            services.AddSingleton<IHostedService, AppHubStreamer>();
            services.AddSingleton<IHostedService, AppInventoryUpdaterHostedService>();
            services.AddSingleton<IHostedService, DynamicDnsHostedService>();
            services.AddSingleton<IHostedService, TorServicesHostedService>();
            services.AddSingleton<IHostedService, PaymentRequestStreamer>();
            services.AddSingleton<IHostedService, WalletReceiveCacheUpdater>();
            services.AddSingleton<IBackgroundJobClient, BackgroundJobClient>();
            services.AddScoped<IAuthorizationHandler, CookieAuthorizationHandler>();
            services.AddScoped<IAuthorizationHandler, OpenIdAuthorizationHandler>();
            services.AddScoped<IAuthorizationHandler, BitpayAuthorizationHandler>();

            services.TryAddSingleton<ExplorerClientProvider>();
            services.TryAddSingleton<Bitpay>(o =>
            {
                if (o.GetRequiredService<BTCPayServerOptions>().NetworkType == NetworkType.Mainnet)
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
            // bundling

            services.AddBtcPayServerAuthenticationSchemes(configuration);
            services.AddAuthorization(o => o.AddBTCPayPolicies());

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

            var rateLimits = new RateLimitService();
            rateLimits.SetZone($"zone={ZoneLimits.Login} rate=5r/min burst=3 nodelay");
            services.AddSingleton(rateLimits);


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
            return services;
        }
        private const long MAX_DEBUG_LOG_FILE_SIZE = 2000000; // If debug log is in use roll it every N MB.
        private static void AddBtcPayServerAuthenticationSchemes(this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddAuthentication()
                .AddCookie()
                .AddBitpayAuthentication();
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
