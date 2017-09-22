using Microsoft.AspNetCore.Hosting;
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
using BTCPayServer.Logging;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Mails;
using Microsoft.Extensions.Configuration;

namespace BTCPayServer.Hosting
{
	public class Startup
	{
		public Startup(IConfiguration conf)
		{
			Configuration = conf;
		}

		public IConfiguration Configuration
		{
			get; set;
		}
		public void ConfigureServices(IServiceCollection services)
		{
			services.ConfigureBTCPayServer(Configuration);

			services.AddIdentity<ApplicationUser, IdentityRole>()
				.AddEntityFrameworkStores<ApplicationDbContext>()
				.AddDefaultTokenProviders();

			services.AddBTCPayServer();
			services.AddMvc();
		}
		public void Configure(
			IApplicationBuilder app,
			IHostingEnvironment env,
			ILoggerFactory loggerFactory)
		{
			if(env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
				app.UseBrowserLink();
			}


			Logs.Configure(loggerFactory);
			app.UsePayServer();
			app.UseStaticFiles();
			app.UseAuthentication();

			app.UseMvc(routes =>
			{
				routes.MapRoute(
					name: "default",
					template: "{controller=Home}/{action=Index}/{id?}");
			});
		}
	}
}
