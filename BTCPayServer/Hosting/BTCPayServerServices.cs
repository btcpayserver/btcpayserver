using BTCPayServer.Configuration;
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
using Microsoft.Data.Sqlite;
using NBXplorer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using BTCPayServer.Services;
using BTCPayServer.Servcices.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Fees;

namespace BTCPayServer.Hosting
{
	public static class BTCPayServerServices
	{
		public static IWebHostBuilder AddPayServer(this IWebHostBuilder builder, BTCPayServerOptions options)
		{
			return
				builder
				.ConfigureServices(c =>
				{
					c.AddDbContext<ApplicationDbContext>(o =>
					{
						var path = Path.Combine(options.DataDir, "sqllite.db");
						o.UseSqlite("Data Source=" + path);
					});
					c.AddSingleton(options);
					c.AddSingleton<BTCPayServerRuntime>(o =>
					{
						var runtime = new BTCPayServerRuntime();
						runtime.Configure(options);
						return runtime;
					});
					c.AddSingleton<Network>(options.Network);
					c.AddSingleton(o => o.GetRequiredService<BTCPayServerRuntime>().TokenRepository);
					c.AddSingleton(o => o.GetRequiredService<BTCPayServerRuntime>().InvoiceRepository);
					c.AddSingleton<ApplicationDbContextFactory>(o => o.GetRequiredService<BTCPayServerRuntime>().DBFactory);
					c.AddSingleton<StoreRepository>();
					c.AddSingleton(o => o.GetRequiredService<BTCPayServerRuntime>().Wallet);
					c.AddSingleton<CurrencyNameTable>();
					c.AddSingleton<IFeeProvider>(o => new NBXplorerFeeProvider()
					{
						Fallback = new FeeRate(100, 1),
						BlockTarget = 20,
						ExplorerClient = o.GetRequiredService<ExplorerClient>()
					});
					c.AddSingleton<ExplorerClient>(o =>
					{
						var runtime = o.GetRequiredService<BTCPayServerRuntime>();
						return runtime.Explorer;
					});
					c.AddSingleton<Bitpay>(o =>
					{
						if(options.Network == Network.Main)
							return new Bitpay(new Key(), new Uri("https://bitpay.com/"));
						else
							return new Bitpay(new Key(), new Uri("https://test.bitpay.com/"));
					});
					c.TryAddSingleton<IRateProvider, BitpayRateProvider>();
					c.AddSingleton<InvoiceWatcher>();
					c.AddSingleton<IHostedService>(o => o.GetRequiredService<InvoiceWatcher>());
					c.AddScoped<IHttpContextAccessor, HttpContextAccessor>();
					c.AddSingleton<IExternalUrlProvider>(o => new FixedExternalUrlProvider(options.ExternalUrl, o.GetRequiredService<IHttpContextAccessor>()));
				})
				.UseUrls(options.GetUrls());
		}

		public static IApplicationBuilder UsePayServer(this IApplicationBuilder app)
		{
			using(var scope = app.ApplicationServices.GetService<IServiceScopeFactory>().CreateScope())
			{
				scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
			}
			app.UseMiddleware<BTCPayMiddleware>();
			return app;
		}
	}


}
