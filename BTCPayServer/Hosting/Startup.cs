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
using Microsoft.AspNetCore.Mvc.Cors.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Net;
using AspNet.Security.OpenIdConnect.Primitives;
using Meziantou.AspNetCore.BundleTagHelpers;
using BTCPayServer.Security;

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

            // Register the OpenIddict services.
            services.AddOpenIddict()
                .AddCore(options =>
                {
                    // Configure OpenIddict to use the Entity Framework Core stores and entities.
                    options.UseEntityFrameworkCore()
                        .UseDbContext<ApplicationDbContext>();
                })

                .AddServer(options =>
                {
                    // Register the ASP.NET Core MVC binder used by OpenIddict.
                    // Note: if you don't call this method, you won't be able to
                    // bind OpenIdConnectRequest or OpenIdConnectResponse parameters.
                    options.UseMvc();

                    // Enable the token endpoint (required to use the password flow).
                    options.EnableTokenEndpoint("/connect/token");
                    options.EnableAuthorizationEndpoint("/connect/authorize");
                    options.EnableAuthorizationEndpoint("/connect/logout");

                    // Allow client applications to use the grant_type=password flow.
                    options.AllowPasswordFlow();
                    options.AllowAuthorizationCodeFlow();
                    // During development, you can disable the HTTPS requirement.
                    options.DisableHttpsRequirement();

                    // Accept token requests that don't specify a client_id.
                    options.AcceptAnonymousClients();
                    options.DisableScopeValidation();

                    options.UseJsonWebTokens();
                    options.AddEphemeralSigningKey();

                });



            services.AddBTCPayServer();
            services.AddMvc(o =>
            {
                o.Filters.Add(new XFrameOptionsAttribute("DENY"));
                o.Filters.Add(new XContentTypeOptionsAttribute("nosniff"));
                o.Filters.Add(new XXSSProtectionAttribute());
                o.Filters.Add(new ReferrerPolicyAttribute("same-origin"));
                //o.Filters.Add(new ContentSecurityPolicyAttribute()
                //{
                //    FontSrc = "'self' https://fonts.gstatic.com/",
                //    ImgSrc = "'self' data:",
                //    DefaultSrc = "'none'",
                //    StyleSrc = "'self' 'unsafe-inline'",
                //    ScriptSrc = "'self' 'unsafe-inline'"
                //});
            });
            services.TryAddScoped<ContentSecurityPolicies>();
            services.Configure<IdentityOptions>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 7;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;            
                // Configure Identity to use the same JWT claims as OpenIddict instead
                // of the legacy WS-Federation claims it uses by default (ClaimTypes),
                // which saves you from doing the mapping in your authorization controller.
                options.ClaimsIdentity.UserNameClaimType = OpenIdConnectConstants.Claims.Name;
                options.ClaimsIdentity.UserIdClaimType = OpenIdConnectConstants.Claims.Subject;
                options.ClaimsIdentity.RoleClaimType = OpenIdConnectConstants.Claims.Role;
            });

            services.AddHangfire((o) =>
            {
                var scope = AspNetCoreJobActivator.Current.BeginScope(null);
                var options = (ApplicationDbContextFactory)scope.Resolve(typeof(ApplicationDbContextFactory));
                options.ConfigureHangfireBuilder(o);
            });
            services.AddCors(o =>
            {
                o.AddPolicy("BitpayAPI", b =>
                {
                    b.AllowAnyMethod().AllowAnyHeader().AllowAnyOrigin();
                });
            });

            // Needed to debug U2F for ledger support
            //services.Configure<KestrelServerOptions>(kestrel =>
            //{
            //    kestrel.Listen(IPAddress.Loopback, 5012, l =>
            //    {
            //        l.UseHttps("devtest.pfx", "toto");
            //    });
            //});
        }

        public void Configure(
            IApplicationBuilder app,
            IHostingEnvironment env,
            IServiceProvider prov,
            BTCPayServerOptions options,
            ILoggerFactory loggerFactory)
        {
            Logs.Configure(loggerFactory);
            Logs.Configuration.LogInformation($"Root Path: {options.RootPath}");
            if (options.RootPath.Equals("/", StringComparison.OrdinalIgnoreCase))
            {
                ConfigureCore(app, env, prov, loggerFactory, options);
            }
            else
            {
                app.Map(options.RootPath, appChild =>
                {
                    ConfigureCore(appChild, env, prov, loggerFactory, options);
                });
            }
        }

        private static void ConfigureCore(IApplicationBuilder app, IHostingEnvironment env, IServiceProvider prov, ILoggerFactory loggerFactory, BTCPayServerOptions options)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseCors();
            app.UsePayServer();
            app.UseStaticFiles();
            app.UseAuthentication();
            app.UseHangfireServer();
            app.UseHangfireDashboard("/hangfire", new DashboardOptions()
            {
                AppPath = options.GetRootUri(),
                Authorization = new[] { new NeedRole(Roles.ServerAdmin) }
            });
            app.UseWebSockets();
            app.UseStatusCodePages();
            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
