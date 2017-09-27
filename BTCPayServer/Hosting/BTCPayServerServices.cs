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

namespace BTCPayServer.Hosting
{
	public static class BTCPayServerServices
	{
		public class OwnStoreAuthorizationRequirement : IAuthorizationRequirement
		{
			public OwnStoreAuthorizationRequirement()
			{
			}

			public OwnStoreAuthorizationRequirement(string role)
			{
				Role = role;
			}

			public string Role
			{
				get; set;
			}
		}

		public class OwnStoreHandler : AuthorizationHandler<OwnStoreAuthorizationRequirement>
		{
			StoreRepository _StoreRepository;
			UserManager<ApplicationUser> _UserManager;
			public OwnStoreHandler(StoreRepository storeRepository, UserManager<ApplicationUser> userManager)
			{
				_StoreRepository = storeRepository;
				_UserManager = userManager;
			}
			protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OwnStoreAuthorizationRequirement requirement)
			{
				object storeId = null;
				if(!((Microsoft.AspNetCore.Mvc.ActionContext)context.Resource).RouteData.Values.TryGetValue("storeId", out storeId))
					context.Succeed(requirement);
				else
				{
					var store = await _StoreRepository.FindStore((string)storeId, _UserManager.GetUserId(((Microsoft.AspNetCore.Mvc.ActionContext)context.Resource).HttpContext.User));
					if(store != null)
						if(requirement.Role == null || requirement.Role == store.Role)
							context.Succeed(requirement);
				}
			}
		}
		class BTCPayServerConfigureOptions : IConfigureOptions<MvcOptions>
		{
			BTCPayServerOptions _Options;
			public BTCPayServerConfigureOptions(BTCPayServerOptions options)
			{
				_Options = options;
			}
			public void Configure(MvcOptions options)
			{
				if(_Options.RequireHttps)
					options.Filters.Add(new RequireHttpsAttribute());
			}
		}
		public static IServiceCollection AddBTCPayServer(this IServiceCollection services)
		{
			services.AddDbContext<ApplicationDbContext>((provider, o) =>
			{
				var path = Path.Combine(provider.GetRequiredService<BTCPayServerOptions>().DataDir, "sqllite.db");
				o.UseSqlite("Data Source=" + path);
			});
			services.TryAddSingleton<SettingsRepository>();
			services.TryAddSingleton<InvoicePaymentNotification>();
			services.TryAddSingleton<BTCPayServerOptions>(o => o.GetRequiredService<IOptions<BTCPayServerOptions>>().Value);
			services.TryAddSingleton<IConfigureOptions<MvcOptions>, BTCPayServerConfigureOptions>();
			services.TryAddSingleton(o =>
			{
				var runtime = new BTCPayServerRuntime();
				runtime.Configure(o.GetRequiredService<BTCPayServerOptions>());
				return runtime;
			});
			services.TryAddSingleton(o => o.GetRequiredService<BTCPayServerRuntime>().TokenRepository);
			services.TryAddSingleton(o => o.GetRequiredService<BTCPayServerRuntime>().InvoiceRepository);
			services.TryAddSingleton<Network>(o => o.GetRequiredService<BTCPayServerOptions>().Network);
			services.TryAddSingleton<ApplicationDbContextFactory>(o => o.GetRequiredService<BTCPayServerRuntime>().DBFactory);
			services.TryAddSingleton<StoreRepository>();
			services.TryAddSingleton(o => o.GetRequiredService<BTCPayServerRuntime>().Wallet);
			services.TryAddSingleton<CurrencyNameTable>();
			services.TryAddSingleton<IFeeProvider>(o => new NBXplorerFeeProvider()
			{
				Fallback = new FeeRate(100, 1),
				BlockTarget = 20,
				ExplorerClient = o.GetRequiredService<ExplorerClient>()
			});
			services.TryAddSingleton<ExplorerClient>(o =>
			{
				var runtime = o.GetRequiredService<BTCPayServerRuntime>();
				return runtime.Explorer;
			});
			services.TryAddSingleton<Bitpay>(o =>
			{
				if(o.GetRequiredService<BTCPayServerOptions>().Network == Network.Main)
					return new Bitpay(new Key(), new Uri("https://bitpay.com/"));
				else
					return new Bitpay(new Key(), new Uri("https://test.bitpay.com/"));
			});
			services.TryAddSingleton<IRateProvider, BitpayRateProvider>();
			services.TryAddSingleton<InvoiceWatcher>();
			services.TryAddSingleton<InvoiceNotificationManager>();
			services.TryAddSingleton<IHostedService>(o => o.GetRequiredService<InvoiceWatcher>());
			services.TryAddScoped<IHttpContextAccessor, HttpContextAccessor>();
			services.TryAddSingleton<IExternalUrlProvider>(o =>
			{
				var op = o.GetRequiredService<BTCPayServerOptions>();
				if(op.ExternalUrl != null)
					return new FixedExternalUrlProvider(op.ExternalUrl, o.GetRequiredService<IHttpContextAccessor>());
				return new DefaultExternalUrlProvider(o.GetRequiredService<IHttpContextAccessor>());
			});
			services.TryAddSingleton<IAuthorizationHandler, OwnStoreHandler>();
			services.AddTransient<AccessTokenController>();
			// Add application services.
			services.AddTransient<IEmailSender, EmailSender>();

			services.AddAuthorization(o =>
			{
				o.AddPolicy("CanAccessStore", builder =>
				{
					builder.AddRequirements(new OwnStoreAuthorizationRequirement());
				});

				o.AddPolicy("OwnStore", builder =>
				{
					builder.AddRequirements(new OwnStoreAuthorizationRequirement("Owner"));
				});
			});

			return services;
		}

		public static IApplicationBuilder UsePayServer(this IApplicationBuilder app)
		{
			if(app.ApplicationServices.GetRequiredService<BTCPayServerOptions>().RequireHttps)
			{
				var options = new RewriteOptions().AddRedirectToHttps();
				app.UseRewriter(options);
			}

			using(var scope = app.ApplicationServices.GetService<IServiceScopeFactory>().CreateScope())
			{
				scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
			}
			app.UseMiddleware<BTCPayMiddleware>();
			return app;
		}
	}


}
