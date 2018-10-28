using BTCPayServer.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Http;
using NBitpayClient;
using NBitcoin;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using System.IO;
using NBXplorer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Fees;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using BTCPayServer.Controllers;
using BTCPayServer.Services.Mails;
using Microsoft.AspNetCore.Identity;
using BTCPayServer.Models;
using System.Threading.Tasks;
using System.Threading;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Authentication;
using Microsoft.Extensions.Caching.Memory;
using BTCPayServer.Logging;
using BTCPayServer.HostedServices;
using Meziantou.AspNetCore.BundleTagHelpers;
using System.Security.Claims;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBXplorer.DerivationStrategy;
using NicolasDorier.RateLimits;
using Npgsql;

namespace BTCPayServer.Hosting
{
    public static class BTCPayServerServices
    {
        public static IServiceCollection AddBTCPayServer(this IServiceCollection services)
        {
            services.AddDbContext<ApplicationDbContext>((provider, o) =>
            {
                var factory = provider.GetRequiredService<ApplicationDbContextFactory>();
                factory.ConfigureBuilder(o);
            });
            services.AddHttpClient();
            services.TryAddSingleton<SettingsRepository>();
            services.TryAddSingleton<InvoicePaymentNotification>();
            services.TryAddSingleton<BTCPayServerOptions>(o => o.GetRequiredService<IOptions<BTCPayServerOptions>>().Value);
            services.TryAddSingleton<InvoiceRepository>(o =>
            {
                var opts = o.GetRequiredService<BTCPayServerOptions>();
                var dbContext = o.GetRequiredService<ApplicationDbContextFactory>();
                var dbpath = Path.Combine(opts.DataDir, "InvoiceDB");
                if (!Directory.Exists(dbpath))
                    Directory.CreateDirectory(dbpath);
                return new InvoiceRepository(dbContext, dbpath);
            });
            services.AddSingleton<BTCPayServerEnvironment>();
            services.TryAddSingleton<TokenRepository>();
            services.TryAddSingleton<EventAggregator>();
            services.TryAddSingleton<CoinAverageSettings>();
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
                    dbContext = new ApplicationDbContextFactory(DatabaseType.MySQL, opts.MySQLConnectionString);
                }
                else
                {
                    var connStr = "Data Source=" + Path.Combine(opts.DataDir, "sqllite.db");
                    Logs.Configuration.LogInformation($"SQLite DB used ({connStr})");
                    dbContext = new ApplicationDbContextFactory(DatabaseType.Sqlite, connStr);
                }
                 
                return dbContext;
            });

            services.TryAddSingleton<BTCPayNetworkProvider>(o => 
            {
                var opts = o.GetRequiredService<BTCPayServerOptions>();
                return opts.NetworkProvider;
            });

            services.TryAddSingleton<AppsHelper>();

            services.TryAddSingleton<LightningConfigurationProvider>();
            services.TryAddSingleton<LanguageService>();
            services.TryAddSingleton<NBXplorerDashboard>();
            services.TryAddSingleton<StoreRepository>();
            services.TryAddSingleton<BTCPayWalletProvider>();
            services.TryAddSingleton<CurrencyNameTable>();
            services.TryAddSingleton<IFeeProviderFactory>(o => new NBXplorerFeeProviderFactory(o.GetRequiredService<ExplorerClientProvider>())
            {
                Fallback = new FeeRate(100, 1),
                BlockTarget = 20
            });

            services.AddSingleton<CssThemeManager>();
            services.Configure<MvcOptions>((o) => {
                o.Filters.Add(new ContentSecurityPolicyCssThemeManager());
                o.ModelMetadataDetailsProviders.Add(new SuppressChildValidationMetadataProvider(typeof(WalletId)));
                o.ModelMetadataDetailsProviders.Add(new SuppressChildValidationMetadataProvider(typeof(DerivationStrategyBase)));
            });
            services.AddSingleton<IHostedService, CssThemeManagerHostedService>();
            services.AddSingleton<IHostedService, MigratorHostedService>();

            services.AddSingleton<Payments.IPaymentMethodHandler<DerivationStrategy>, Payments.Bitcoin.BitcoinLikePaymentHandler>();
            services.AddSingleton<IHostedService, Payments.Bitcoin.NBXplorerListener>();

            services.AddSingleton<IHostedService, HostedServices.CheckConfigurationHostedService>();

            services.AddSingleton<Payments.IPaymentMethodHandler<Payments.Lightning.LightningSupportedPaymentMethod>, Payments.Lightning.LightningLikePaymentHandler>();
            services.AddSingleton<IHostedService, Payments.Lightning.LightningListener>();
            
            services.AddSingleton<ChangellyClientProvider>();

            services.AddSingleton<IHostedService, NBXplorerWaiters>();
            services.AddSingleton<IHostedService, InvoiceNotificationManager>();
            services.AddSingleton<IHostedService, InvoiceWatcher>();
            services.AddSingleton<IHostedService, RatesHostedService>();
            services.AddTransient<IConfigureOptions<MvcOptions>, BTCPayClaimsFilter>();

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
            // Add application services.
            services.AddTransient<IEmailSender, EmailSender>();
            // bundling

            services.AddAuthorization(o => Policies.AddBTCPayPolicies(o));
            BitpayAuthentication.AddAuthentication(services);

            services.AddBundles();
            services.AddTransient<BundleOptions>(provider =>
            {
                var opts = provider.GetRequiredService<BTCPayServerOptions>();
                var bundle = new BundleOptions();
                bundle.UseBundles = opts.BundleJsCss;
                bundle.AppendVersion = true;
                return bundle;
            });

            services.AddCors(options=> 
            {
                options.AddPolicy(CorsPolicies.All, p=>p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
            });

            var rateLimits = new RateLimitService();
            rateLimits.SetZone($"zone={ZoneLimits.Login} rate=5r/min burst=3 nodelay");
            services.AddSingleton(rateLimits);
            return services;
        }

        public static IApplicationBuilder UsePayServer(this IApplicationBuilder app)
        {
            using (var scope = app.ApplicationServices.GetService<IServiceScopeFactory>().CreateScope())
            {
                //Wait the DB is ready
                Retry(() =>
                {
                    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
                });
            }

            app.UseMiddleware<BTCPayMiddleware>();
            return app; 
        }

        static void Retry(Action act)
        {
            CancellationTokenSource cts = new CancellationTokenSource(1000);
            while (true)
            {
                try
                {
                    act();
                    return;
                }
                // Starting up
                catch (PostgresException ex) when (ex.SqlState == "57P03") { Thread.Sleep(1000); }
                catch when (!cts.IsCancellationRequested)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }


}
