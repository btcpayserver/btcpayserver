using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;

using Microsoft.AspNetCore.Builder;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using BTCPayServer.Data;
using Microsoft.Extensions.Logging;
using BTCPayServer.Logging;
using Microsoft.Extensions.Configuration;
using BTCPayServer.Configuration;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Net.Http.Headers;
using System.Net;
using System.Threading.Tasks;
using BTCPayServer.JsonConverters;
using BTCPayServer.PaymentRequest;
using BTCPayServer.Services.Apps;
using BTCPayServer.Storage;
using BTCPayServerDockerConfigurator;
using BTCPayServerDockerConfigurator.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Primitives;
using NBXplorer;

namespace BTCPayServer.Hosting
{
    public class Startup
    {
        public Startup(IConfiguration conf, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            Configuration = conf;
            _Env = env;
            LoggerFactory = loggerFactory;
        }
        IWebHostEnvironment _Env;
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
            services.AddDataProtection()
                .SetApplicationName("BTCPay Server")
                .PersistKeysToFileSystem(GetDataDir());
            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            services.AddBTCPayServer(Configuration);
            
            services.AddProviderStorage();
            services.AddSession();
            services.AddSignalR();
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
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                var builtInFactory = options.InvalidModelStateResponseFactory;

                options.InvalidModelStateResponseFactory = context =>
                {
                    context.HttpContext.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
                    return builtInFactory(context);
                };
            })
            .AddNewtonsoftJson()
#if RAZOR_RUNTIME_COMPILE
            .AddRazorRuntimeCompilation()
#endif
            .AddControllersAsServices()
            .AddConfigurator(services, Configuration);
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
        private DirectoryInfo GetDataDir()
        {
            return new DirectoryInfo(Configuration.GetDataDir(DefaultConfiguration.GetNetworkType(Configuration)));
        }

        private static void ConfigureCore(IApplicationBuilder app, IWebHostEnvironment env, IServiceProvider prov, ILoggerFactory loggerFactory, BTCPayServerOptions options)
        {
            Logs.Configure(loggerFactory);
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseHeadersOverride();
            var forwardingOptions = new ForwardedHeadersOptions()
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            };
            forwardingOptions.KnownNetworks.Clear();
            forwardingOptions.KnownProxies.Clear();
            forwardingOptions.ForwardedHeaders = ForwardedHeaders.All;
            app.UseForwardedHeaders(forwardingOptions);


            app.UseStatusCodePagesWithReExecute("/Error/Handle", "?statusCode={0}");

            app.UsePayServer();
            app.UseRouting();
            app.UseCors();

            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    // Cache static assets for one year, set asp-append-version="true" on references to update on change.
                    // https://andrewlock.net/adding-cache-control-headers-to-static-files-in-asp-net-core/
                    const int durationInSeconds = 60 * 60 * 24 * 365;
                    ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=" + durationInSeconds;
                }
            });
            
            app.UseProviderStorage(options);
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseSession();

            app.UseWebSockets();

            app.UseEndpoints(endpoints =>
            {
                AppHub.Register(endpoints);
                PaymentRequestHub.Register(endpoints);
                endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
                var x = new X( endpoints.DataSources.SelectMany(source =>source.Endpoints).ToList(), new Y(endpoints.DataSources.Select(source =>  source.GetChangeToken() ).ToList()));
                endpoints.DataSources.Clear();
                //
                //
                // endpoints.MapControllerRoute("configurator", "configurator/{action}", new
                // {
                //     controller = "Configurator"
                // });
                endpoints.DataSources.Add(x);
                
                
            });
        }
    }

    class X : EndpointDataSource
    {
        private readonly Y _changeToken;
        private readonly List<Endpoint> _endpoints;

        public X(List<Endpoint> endpoints, Y changeToken  )
        {
            _changeToken = changeToken;
            var configEndpoints = endpoints.Where(endpoint => endpoint.Metadata.Any(o =>
                o is ControllerActionDescriptor actionDescriptor && actionDescriptor.ControllerTypeInfo.Namespace ==
                typeof(ConfiguratorController).Namespace)).ToList();
            _endpoints = endpoints.Where(endpoint => !configEndpoints.Contains(endpoint)).ToList();
            _endpoints.AddRange(configEndpoints.Select(endpoint =>
            {
                if (endpoint is RouteEndpoint routeEndpoint)
                {
                    var builder = new RouteEndpointBuilder(context => Task.CompletedTask,
                        RoutePatternFactory.Parse($"configurator/{routeEndpoint.RoutePattern.RawText}"),
                        routeEndpoint.Order);
                    return builder.Build();

                }

                return endpoint;
            }));
        }
        public override IChangeToken GetChangeToken()
        {
            return _changeToken;
        }

        public override IReadOnlyList<Endpoint> Endpoints
        {
            get
            {
                return _endpoints;
            }
        }
    }
    
    class Y: IChangeToken
    {
        private readonly IEnumerable<IChangeToken> _changeTokens;

        public Y(IEnumerable<IChangeToken> changeTokens)
        {
            _changeTokens = changeTokens;
        }
        public IDisposable RegisterChangeCallback(Action<object> callback, object state)
        {
            var result = new CompositeDisposable();
            foreach (IChangeToken changeToken in _changeTokens)
            {
                result.Add(changeToken.RegisterChangeCallback(callback, state));
            }
            return result;
        }

        public bool HasChanged { get; }
        public bool ActiveChangeCallbacks { get; }

    }
}
