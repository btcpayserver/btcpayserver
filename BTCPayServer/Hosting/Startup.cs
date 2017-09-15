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

namespace BTCPayServer.Hosting
{
	public class Startup
	{
		public void ConfigureServices(IServiceCollection services)
		{

			services.AddIdentity<ApplicationUser, IdentityRole>()
				.AddEntityFrameworkStores<ApplicationDbContext>()
				.AddDefaultTokenProviders();

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
			services.AddSingleton<IAuthorizationHandler, OwnStoreHandler>();
			services.AddTransient<AccessTokenController>();
			// Add application services.
			services.AddTransient<IEmailSender, EmailSender>();

			//services.AddSingleton<IObjectModelValidator, NoObjectModelValidator>();
			services.AddMvcCore(o =>
			{
				//o.Filters.Add(new NBXplorerExceptionFilter());
				o.OutputFormatters.Clear();
				o.InputFormatters.Clear();
			})
				.AddJsonFormatters()
				.AddFormatterMappings();

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
}
