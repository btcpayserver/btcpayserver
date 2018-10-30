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
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;
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

            // Require that all the required options for a HTTPS certificate be set in the config file.
            // If the HTTPS options can't be used don't use this mechanism and instead allow the defautl Kestrel bindings to occur.
            var networkType = DefaultConfiguration.GetNetworkType(Configuration);
            var defaultSettings = BTCPayDefaultSettings.GetDefaultSettings(networkType);
            var btcPaySettings = Configuration.Get<BTCPayServerOptions>();
            var bindAddress = Configuration.GetOrDefault<IPAddress>("bind", IPAddress.None);
            int bindPort = Configuration.GetOrDefault<int>("port", 0);
            string httpsCertificateFilePath = btcPaySettings.HttpsCertificateFilePath;

            if (bindAddress != IPAddress.None && bindPort != 0 && !String.IsNullOrEmpty(httpsCertificateFilePath))
            {
                services.Configure<KestrelServerOptions>(kestrel =>
                {
                    if (!File.Exists(btcPaySettings.HttpsCertificateFilePath))
                    {
                        // Note that by design this is a fatal error condition that will cause the process to exit.
                        throw new ConfigException($"The https certificate file could not be found at {btcPaySettings.HttpsCertificateFilePath}.");
                    }

                    Logs.Configuration.LogInformation($"Https certificate file path {btcPaySettings.HttpsCertificateFilePath}.");
                    kestrel.Listen(bindAddress, bindPort, l =>
                    {
                        l.UseHttps(btcPaySettings.HttpsCertificateFilePath, btcPaySettings.HttpsCertificateFilePassword);
                    });
                });
            }
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
