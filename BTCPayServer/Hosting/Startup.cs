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
using BTCPayServer.Logging;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Mails;
using Microsoft.Extensions.Configuration;
using BTCPayServer.Configuration;
using System.IO;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Threading;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc.Cors.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Net;
using BTCPayServer.Hubs;
using Meziantou.AspNetCore.BundleTagHelpers;
using BTCPayServer.Security;

namespace BTCPayServer.Hosting
{
    public class Startup
    {
        public Startup(IConfiguration conf, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            Configuration = conf;
            _Env = env;
            LoggerFactory = loggerFactory;
        }
        IHostingEnvironment _Env;
        public IConfiguration Configuration
        {
            get; set;
        }
        public ILoggerFactory LoggerFactory { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            Logs.Configure(LoggerFactory);
            services.ConfigureBTCPayServer(Configuration);
            services.AddMemoryCache();
            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();
            services.AddSignalR();
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
                options.Password.RequiredLength = 6;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;
            });
            // If the HTTPS certificate path is not set this logic will NOT be used and the default Kestrel binding logic will be.
            string httpsCertificateFilePath = Configuration.GetOrDefault<string>("HttpsCertificateFilePath", null);
            bool useDefaultCertificate = Configuration.GetOrDefault<bool>("HttpsUseDefaultCertificate", false);
            bool hasCertPath = !String.IsNullOrEmpty(httpsCertificateFilePath);
            if (hasCertPath || useDefaultCertificate)
            {
                var bindAddress = Configuration.GetOrDefault<IPAddress>("bind", IPAddress.Any);
                int bindPort = Configuration.GetOrDefault<int>("port", 443);

                services.Configure<KestrelServerOptions>(kestrel =>
                {
                    if (hasCertPath && !File.Exists(httpsCertificateFilePath))
                    {
                        // Note that by design this is a fatal error condition that will cause the process to exit.
                        throw new ConfigException($"The https certificate file could not be found at {httpsCertificateFilePath}.");
                    }
                    if(hasCertPath && useDefaultCertificate)
                    {
                        throw new ConfigException($"Conflicting settings: if HttpsUseDefaultCertificate is true, HttpsCertificateFilePath should not be used");
                    }

                    kestrel.Listen(bindAddress, bindPort, l =>
                    {
                        if (hasCertPath)
                        {
                            Logs.Configuration.LogInformation($"Using HTTPS with the certificate located in {httpsCertificateFilePath}.");
                            l.UseHttps(httpsCertificateFilePath, Configuration.GetOrDefault<string>("HttpsCertificateFilePassword", null));
                        }
                        else
                        {
                            Logs.Configuration.LogInformation($"Using HTTPS with the default certificate");
                            l.UseHttps();
                        }
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
            app.UseSignalR(route =>
            {
                route.MapHub<CrowdfundHub>("/apps/crowdfund/hub");
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
