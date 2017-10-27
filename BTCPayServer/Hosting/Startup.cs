using Microsoft.AspNetCore.Hosting;
using System.Reflection;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc;
using NBitpayClient;
using BTCPayServer.Authentication;
using Microsoft.EntityFrameworkCore;
using BTCPayServer.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using BTCPayServer.Services;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Identity;
using BTCPayServer.Data;
using Microsoft.Extensions.Logging;
using Hangfire;
using BTCPayServer.Logging;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Mails;
using Microsoft.Extensions.Configuration;
using Hangfire.AspNetCore;
using BTCPayServer.Configuration;
using System.IO;
using Hangfire.Dashboard;
using Hangfire.Annotations;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Threading;
using Microsoft.Extensions.Options;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Mvc.Cors.Internal;

namespace BTCPayServer.Hosting
{
	public class Startup
	{
		class NeedRole : IDashboardAuthorizationFilter
		{
			string _Role;
			public NeedRole(string role)
			{
				_Role = role;
			}
			public bool Authorize([NotNull] DashboardContext context)
			{
				return context.GetHttpContext().User.IsInRole(_Role);
			}
		}
		public Startup(IConfiguration conf, IHostingEnvironment env)
		{
			Configuration = conf;
			_Env = env;
		}
		IHostingEnvironment _Env;
		public IConfiguration Configuration
		{
			get; set;
		}
		
		public void ConfigureServices(IServiceCollection services)
		{
			services.ConfigureBTCPayServer(Configuration);
			services.AddMemoryCache();
			services.AddIdentity<ApplicationUser, IdentityRole>()
				.AddEntityFrameworkStores<ApplicationDbContext>()
				.AddDefaultTokenProviders();

			// Big hack, tests fails because Hangfire fail at initializing at the second test run
			AddHangfireFix(services);
			services.AddBTCPayServer();
			services.AddMvc(o =>
			{
				o.Filters.Add(new XFrameOptionsAttribute("DENY"));
			});
		}

		// Big hack, tests fails if only call AddHangfire because Hangfire fail at initializing at the second test run
		private void AddHangfireFix(IServiceCollection services)
		{
			Action<IGlobalConfiguration> configuration = o =>
			{
				var scope = AspNetCoreJobActivator.Current.BeginScope(null);
				var options = (ApplicationDbContextFactory)scope.Resolve(typeof(ApplicationDbContextFactory));
				options.ConfigureHangfireBuilder(o);
			};

			ServiceCollectionDescriptorExtensions.TryAddSingleton<Action<IGlobalConfiguration>>(services, (IServiceProvider serviceProvider) => new Action<IGlobalConfiguration>((config) =>
		   {
			   ILoggerFactory service = ServiceProviderServiceExtensions.GetService<ILoggerFactory>(serviceProvider);
			   if(service != null)
			   {
				   Hangfire.GlobalConfigurationExtensions.UseLogProvider<AspNetCoreLogProvider>(config, new AspNetCoreLogProvider(service));
			   }
			   IServiceScopeFactory service2 = ServiceProviderServiceExtensions.GetService<IServiceScopeFactory>(serviceProvider);
			   if(service2 != null)
			   {
				   Hangfire.GlobalConfigurationExtensions.UseActivator<AspNetCoreJobActivator>(config, new AspNetCoreJobActivator(service2));
			   }
			   configuration(config);
		   }));

			services.AddHangfire(configuration);
			services.AddCors(o =>
			{
				o.AddPolicy("BitpayAPI", b =>
				{
					b.AllowAnyMethod().AllowAnyHeader().AllowAnyOrigin();
				});
			});

			services.Configure<IOptions<ApplicationInsightsServiceOptions>>(o =>
			{
				o.Value.DeveloperMode = _Env.IsDevelopment();
			});
		}

		public void Configure(
			IApplicationBuilder app,
			IHostingEnvironment env,
			IServiceProvider prov,
			ILoggerFactory loggerFactory)
		{
			if(env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
				app.UseBrowserLink();
			}


			Logs.Configure(loggerFactory);

			//App insight do not that by itself...
			loggerFactory.AddApplicationInsights(prov, LogLevel.Information);

			app.UsePayServer();
			app.UseStaticFiles();
			app.UseAuthentication();
			app.UseHangfireServer();
			app.UseHangfireDashboard("/hangfire", new DashboardOptions() { Authorization = new[] { new NeedRole(Roles.ServerAdmin) } });
			app.UseMvc(routes =>
			{
				routes.MapRoute(
					name: "default",
					template: "{controller=Home}/{action=Index}/{id?}");
			});
		}
	}
}
