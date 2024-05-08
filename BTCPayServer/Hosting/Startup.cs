using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.Fido2;
using BTCPayServer.Filters;
using BTCPayServer.Logging;
using BTCPayServer.PaymentRequest;
using BTCPayServer.Plugins;
using BTCPayServer.Security;
using BTCPayServer.Services.Apps;
using BTCPayServer.Storage;
using Fido2NetLib;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using NBXplorer;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Hosting
{
    public class Startup
    {
        public Startup(IConfiguration conf, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            Configuration = conf;
            _Env = env;
            LoggerFactory = loggerFactory;
            Logs = new Logs();
            Logs.Configure(loggerFactory);
        }

        readonly IWebHostEnvironment _Env;
        public IConfiguration Configuration
        {
            get; set;
        }
        public ILoggerFactory LoggerFactory { get; }
        public Logs Logs { get; }

        public static ServiceProvider CreateBootstrap(IConfiguration conf)
        {
            return CreateBootstrap(conf, new Logs(), new FuncLoggerFactory(n => NullLogger.Instance));
        }
        public static ServiceProvider CreateBootstrap(IConfiguration conf, Logs logs, ILoggerFactory loggerFactory)
        {
            ServiceCollection bootstrapServices = new ServiceCollection();
            var networkType = DefaultConfiguration.GetNetworkType(conf);
            bootstrapServices.AddSingleton(logs);
            bootstrapServices.AddSingleton(loggerFactory);
            bootstrapServices.AddSingleton<IConfiguration>(conf);
            bootstrapServices.AddSingleton<SelectedChains>();
            bootstrapServices.AddSingleton<NBXplorerNetworkProvider>(new NBXplorerNetworkProvider(networkType));
            return bootstrapServices.BuildServiceProvider();
        }

        public void ConfigureServices(IServiceCollection services)
        {
            var bootstrapServiceProvider = CreateBootstrap(Configuration, Logs, LoggerFactory);
            services.AddSingleton(bootstrapServiceProvider.GetRequiredService<SelectedChains>());
            services.AddSingleton(bootstrapServiceProvider.GetRequiredService<NBXplorerNetworkProvider>());

            services.AddMemoryCache();
            services.AddDataProtection()
                .SetApplicationName("BTCPay Server")
                .PersistKeysToFileSystem(new DirectoryInfo(new DataDirectories().Configure(Configuration).DataDir));
            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders()
                .AddInvitationTokenProvider();
            services.Configure<AuthenticationOptions>(opts =>
            {
                opts.DefaultAuthenticateScheme = null;
                opts.DefaultChallengeScheme = null;
                opts.DefaultForbidScheme = null;
                opts.DefaultScheme = IdentityConstants.ApplicationScheme;
                opts.DefaultSignInScheme = null;
                opts.DefaultSignOutScheme = null;
            });
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, opt =>
            {
                opt.LoginPath = "/login";
                opt.AccessDeniedPath = "/errors/403";
                opt.LogoutPath = "/logout";
            });

            services.Configure<SecurityStampValidatorOptions>(opts =>
            {
                opts.ValidationInterval = TimeSpan.FromMinutes(5.0);
            });

            services.AddBTCPayServer(Configuration, Logs);
            services.AddProviderStorage();
            services.AddSession();
            services.AddSignalR().AddNewtonsoftJsonProtocol();
            services.AddFido2(options =>
                {
                    options.ServerName = "BTCPay Server";
                })
                .AddCachedMetadataService(config =>
                {
                    //They'll be used in a "first match wins" way in the order registered
                    config.AddStaticMetadataRepository();
                });
            var descriptor = services.Single(descriptor => descriptor.ServiceType == typeof(Fido2Configuration));
            services.Remove(descriptor);
            services.AddScoped(provider =>
            {
                var httpContext = provider.GetService<IHttpContextAccessor>();
                return new Fido2Configuration()
                {
                    ServerName = "BTCPay Server",
                    Origin = $"{httpContext.HttpContext.Request.Scheme}://{httpContext.HttpContext.Request.Host}",
                    ServerDomain = httpContext.HttpContext.Request.Host.Host
                };
            });
            services.AddScoped<Fido2Service>();
            services.AddSingleton<UserLoginCodeService>();
            services.AddSingleton<LnurlAuthService>();
            services.AddSingleton<LightningAddressService>();
            services.AddMvc(o =>
             {
                 o.Filters.Add(new XFrameOptionsAttribute(XFrameOptionsAttribute.XFrameOptions.Deny));
                 o.Filters.Add(new XContentTypeOptionsAttribute("nosniff"));
                 o.Filters.Add(new XXSSProtectionAttribute());
                 o.Filters.Add(new ReferrerPolicyAttribute("same-origin"));
                 o.ModelBinderProviders.Insert(0, new ModelBinders.DefaultModelBinderProvider());
                 if (!Configuration.GetOrDefault<bool>("nocsp", false))
                     o.Filters.Add(new ContentSecurityPolicyAttribute(CSPTemplate.AntiXSS));
                 o.Filters.Add(new JsonHttpExceptionFilter());
                 o.Filters.Add(new JsonObjectExceptionFilter());
             })
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    return new UnprocessableEntityObjectResult(context.ModelState.ToGreenfieldValidationError());
                };
            })
            .AddRazorOptions(o =>
            {
                // /Components/{View Component Name}/{View Name}.cshtml
                o.ViewLocationFormats.Add("/{0}.cshtml");
                o.PageViewLocationFormats.Add("/{0}.cshtml");
            })
            .AddNewtonsoftJson()
            .AddRazorRuntimeCompilation()
            .AddPlugins(services, Configuration, LoggerFactory, bootstrapServiceProvider)
            .AddControllersAsServices();

            services.AddServerSideBlazor();

            LowercaseTransformer.Register(services);
            ValidateControllerNameTransformer.Register(services);

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
                options.Password.RequireUppercase = false;
            });
            // If the HTTPS certificate path is not set this logic will NOT be used and the default Kestrel binding logic will be.
            string httpsCertificateFilePath = Configuration.GetOrDefault<string>("HttpsCertificateFilePath", null);
            bool useDefaultCertificate = Configuration.GetOrDefault<bool>("HttpsUseDefaultCertificate", false);
            bool hasCertPath = !String.IsNullOrEmpty(httpsCertificateFilePath);
            services.Configure<KestrelServerOptions>(kestrel =>
            {
                kestrel.Limits.MaxRequestLineSize = 8_192 * 10 * 5; // Around 500K, transactions passed in URI should not be bigger than this
            });
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
                    if (hasCertPath && useDefaultCertificate)
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
            IWebHostEnvironment env,
            IServiceProvider prov,
            BTCPayServerOptions options,
            IOptions<DataDirectories> dataDirectories,
            ILoggerFactory loggerFactory,
            IRateLimitService rateLimits)
        {
            Logs.Configure(loggerFactory);
            Logs.Configuration.LogInformation($"Root Path: {options.RootPath}");
            if (options.RootPath.Equals("/", StringComparison.OrdinalIgnoreCase))
            {
                ConfigureCore(app, env, prov, dataDirectories, rateLimits);
            }
            else
            {
                app.Map(options.RootPath, appChild =>
                {
                    ConfigureCore(appChild, env, prov, dataDirectories, rateLimits);
                });
            }
        }
        private void ConfigureCore(IApplicationBuilder app, IWebHostEnvironment env, IServiceProvider prov, IOptions<DataDirectories> dataDirectories, IRateLimitService rateLimits)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                rateLimits.SetZone($"zone={ZoneLimits.Login} rate=1000r/min burst=100 nodelay");
                rateLimits.SetZone($"zone={ZoneLimits.PublicInvoices} rate=1000r/min burst=100 nodelay");
                rateLimits.SetZone($"zone={ZoneLimits.Register} rate=1000r/min burst=100 nodelay");
                rateLimits.SetZone($"zone={ZoneLimits.PayJoin} rate=1000r/min burst=100 nodelay");
                rateLimits.SetZone($"zone={ZoneLimits.Shopify} rate=1000r/min burst=100 nodelay");
                rateLimits.SetZone($"zone={ZoneLimits.ForgotPassword} rate=5r/d burst=3 nodelay");
            }
            else
            {
                rateLimits.SetZone($"zone={ZoneLimits.Login} rate=5r/min burst=3 nodelay");
                rateLimits.SetZone($"zone={ZoneLimits.PublicInvoices} rate=4r/min burst=10 delay=3");
                rateLimits.SetZone($"zone={ZoneLimits.Register} rate=2r/min burst=2 nodelay");
                rateLimits.SetZone($"zone={ZoneLimits.PayJoin} rate=5r/min burst=3 nodelay");
                rateLimits.SetZone($"zone={ZoneLimits.Shopify} rate=20r/min burst=3 nodelay");
                rateLimits.SetZone($"zone={ZoneLimits.ForgotPassword} rate=5r/d burst=5 nodelay");
            }

            // HACK: blazor server js hard code some path, making it works only on root path. This fix it.
            // Workaround this bug https://github.com/dotnet/aspnetcore/issues/43191
            var rewriteOptions = new RewriteOptions();
            rewriteOptions.AddRewrite("_blazor/(negotiate|initializers|disconnect)$", "/_blazor/$1", skipRemainingRules: true);
            rewriteOptions.AddRewrite("_blazor$", "/_blazor", skipRemainingRules: true);
            app.UseRewriter(rewriteOptions);

            app.UseHeadersOverride();
            var forwardingOptions = new ForwardedHeadersOptions()
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            };
            forwardingOptions.KnownNetworks.Clear();
            forwardingOptions.KnownProxies.Clear();
            forwardingOptions.ForwardedHeaders = ForwardedHeaders.All;
            app.UseForwardedHeaders(forwardingOptions);

            app.UseStatusCodePagesWithReExecute("/errors/{0}");

            app.UsePayServer();
            app.UseRouting();
            app.UseCors();

            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = LongCache
            });

            // The framework during publish automatically publish the js files into
            // wwwroot, so this shouldn't be needed.
            // But somehow during debug the collocated js files, are error 404!
            var componentsFolder = Path.Combine(env.ContentRootPath, "Components");
            if (Directory.Exists(componentsFolder))
            {
                app.UseStaticFiles(new StaticFileOptions()
                {
                    FileProvider = new PhysicalFileProvider(componentsFolder),
                    RequestPath = "/Components"
                });
            }

            app.UseProviderStorage(dataDirectories);
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseSession();

            app.UseWebSockets();

            app.UseCookiePolicy(new CookiePolicyOptions()
            {
                HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always,
                Secure = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest
            });

            app.UseEndpoints(endpoints =>
            {
                AppHub.Register(endpoints);
                PaymentRequestHub.Register(endpoints);
                endpoints.MapBlazorHub().RequireAuthorization();
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapControllerRoute("default", "{controller:validate=UIHome}/{action:lowercase=Index}/{id?}");
            });
            app.UsePlugins();
        }

        private static void LongCache(Microsoft.AspNetCore.StaticFiles.StaticFileResponseContext ctx)
        {
            // Cache static assets for one year, set asp-append-version="true" on references to update on change.
            // https://andrewlock.net/adding-cache-control-headers-to-static-files-in-asp-net-core/
            const int durationInSeconds = 60 * 60 * 24 * 365;
            ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=" + durationInSeconds;
        }

        private static Action<Microsoft.AspNetCore.StaticFiles.StaticFileResponseContext> NewMethod()
        {
            return ctx =>
            {
                // Cache static assets for one year, set asp-append-version="true" on references to update on change.
                // https://andrewlock.net/adding-cache-control-headers-to-static-files-in-asp-net-core/
                const int durationInSeconds = 60 * 60 * 24 * 365;
                ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=" + durationInSeconds;
            };
        }
    }
}
