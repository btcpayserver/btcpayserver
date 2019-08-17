using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using System;
using System.Globalization;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using AspNet.Security.OpenIdConnect.Primitives;
using BTCPayServer.Authentication.OpenId.Models;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using System.Net;
using BTCPayServer.Authentication.OpenId;
using BTCPayServer.PaymentRequest;
using BTCPayServer.Services.Apps;
using BTCPayServer.Storage;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.Options;

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
            get;
            set;
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

            ConfigureOpenIddict(services);

            services.AddBTCPayServer(Configuration);
            services.AddProviderStorage();
            services.AddSession();
            services.AddSignalR();
            services.AddJsonLocalization(options => options.ResourcesPath = "Resources");
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
                }).AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
                .AddDataAnnotationsLocalization()
                .AddControllersAsServices();


            // Configure supported cultures and localization options
            services.Configure<RequestLocalizationOptions>(options =>
            {
                // TODO auto detect available locales by checking which JSON files we have in the "Resources" dir
                var supportedCultures = new[]
                {
                    new CultureInfo("en-US"), new CultureInfo("nl")
                };

                // State what the default culture for your application is. This will be used if no specific culture
                // can be determined for a given request.
                options.DefaultRequestCulture = new RequestCulture(culture: "en-US");

                // You must explicitly state which cultures your application supports.
                // These are the cultures the app supports for formatting numbers, dates, etc.
                options.SupportedCultures = supportedCultures;

                // These are the cultures the app supports for UI strings, i.e. we have localized resources for.
                options.SupportedUICultures = supportedCultures;

                
                //options.RequestCultureProviders
                
                // TODO add our own culture provider based on the logged in user's profile
                //options.RequestCultureProviders.Add(new UserProfileRequestCultureProvider()); // Add your custom culture provider back to the list
                    
                // You can change which providers are configured to determine the culture for requests, or even add a custom
                // provider with your own logic. The providers will be asked in order to provide a culture for each request,
                // and the first to provide a non-null result that is in the configured supported cultures list will be used.
                // By default, the following built-in providers are configured:
                // - QueryStringRequestCultureProvider, sets culture via "culture" and "ui-culture" query string values, useful for testing
                // - CookieRequestCultureProvider, sets culture via "ASPNET_CULTURE" cookie
                // - AcceptLanguageHeaderRequestCultureProvider, sets culture via the "Accept-Language" request header
//                options.RequestCultureProviders.Insert(0, new CustomRequestCultureProvider(async context =>
//                {
//                    // My custom request culture logic
//                    return new ProviderCultureResult("en");
//                }));
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
                options.Password.RequireUppercase = false;
                // Configure Identity to use the same JWT claims as OpenIddict instead
                // of the legacy WS-Federation claims it uses by default (ClaimTypes),
                // which saves you from doing the mapping in your authorization controller.
                options.ClaimsIdentity.UserNameClaimType = OpenIdConnectConstants.Claims.Name;
                options.ClaimsIdentity.UserIdClaimType = OpenIdConnectConstants.Claims.Subject;
                options.ClaimsIdentity.RoleClaimType = OpenIdConnectConstants.Claims.Role;
            });
            // If the HTTPS certificate path is not set this logic will NOT be used and the default Kestrel binding logic will be.
            string httpsCertificateFilePath = Configuration.GetOrDefault<string>("HttpsCertificateFilePath", null);
            bool useDefaultCertificate = Configuration.GetOrDefault<bool>("HttpsUseDefaultCertificate", false);
            bool hasCertPath = !String.IsNullOrEmpty(httpsCertificateFilePath);
            services.Configure<KestrelServerOptions>(kestrel =>
            {
                kestrel.Limits.MaxRequestLineSize =
                    8_192 * 10 * 5; // Around 500K, transactions passed in URI should not be bigger than this
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
                        throw new ConfigException(
                            $"The https certificate file could not be found at {httpsCertificateFilePath}.");
                    }

                    if (hasCertPath && useDefaultCertificate)
                    {
                        throw new ConfigException(
                            $"Conflicting settings: if HttpsUseDefaultCertificate is true, HttpsCertificateFilePath should not be used");
                    }

                    kestrel.Listen(bindAddress, bindPort, l =>
                    {
                        if (hasCertPath)
                        {
                            Logs.Configuration.LogInformation(
                                $"Using HTTPS with the certificate located in {httpsCertificateFilePath}.");
                            l.UseHttps(httpsCertificateFilePath,
                                Configuration.GetOrDefault<string>("HttpsCertificateFilePassword", null));
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

        private void ConfigureOpenIddict(IServiceCollection services)
        {
// Register the OpenIddict services.
            services.AddOpenIddict()
                .AddCore(options =>
                {
                    // Configure OpenIddict to use the Entity Framework Core stores and entities.
                    options.UseEntityFrameworkCore()
                        .UseDbContext<ApplicationDbContext>()
                        .ReplaceDefaultEntities<BTCPayOpenIdClient, BTCPayOpenIdAuthorization, OpenIddictScope<string>,
                            BTCPayOpenIdToken, string>();
                })
                .AddServer(options =>
                {
                    //Disabled so that Tor works with OpenIddict too
                    options.DisableHttpsRequirement();
                    // Register the ASP.NET Core MVC binder used by OpenIddict.
                    // Note: if you don't call this method, you won't be able to
                    // bind OpenIdConnectRequest or OpenIdConnectResponse parameters.
                    options.UseMvc();

                    // Enable the token endpoint (required to use the password flow).
                    options.EnableTokenEndpoint("/connect/token");
                    options.EnableAuthorizationEndpoint("/connect/authorize");
                    options.EnableLogoutEndpoint("/connect/logout");

                    //we do not care about these granular controls for now
                    options.DisableScopeValidation();
                    options.IgnoreEndpointPermissions();
                    // Allow client applications various flows
                    options.AllowImplicitFlow();
                    options.AllowClientCredentialsFlow();
                    options.AllowRefreshTokenFlow();
                    options.AllowPasswordFlow();
                    options.AllowAuthorizationCodeFlow();
                    options.UseRollingTokens();
                    options.UseJsonWebTokens();

                    options.RegisterScopes(
                        OpenIdConnectConstants.Scopes.OpenId,
                        OpenIdConnectConstants.Scopes.OfflineAccess,
                        OpenIdConnectConstants.Scopes.Email,
                        OpenIdConnectConstants.Scopes.Profile,
                        OpenIddictConstants.Scopes.Roles);
                    options.AddEventHandler<PasswordGrantTypeEventHandler>();
                    options.AddEventHandler<AuthorizationCodeGrantTypeEventHandler>();
                    options.AddEventHandler<RefreshTokenGrantTypeEventHandler>();
                    options.AddEventHandler<ClientCredentialsGrantTypeEventHandler>();
                    options.AddEventHandler<AuthorizationEventHandler>();
                    options.AddEventHandler<LogoutEventHandler>();

                    options.ConfigureSigningKey(Configuration);
                });
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

        private static void ConfigureCore(IApplicationBuilder app, IHostingEnvironment env, IServiceProvider prov,
            ILoggerFactory loggerFactory, BTCPayServerOptions options)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseCors();

            var forwardingOptions = new ForwardedHeadersOptions()
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            };
            forwardingOptions.KnownNetworks.Clear();
            forwardingOptions.KnownProxies.Clear();
            forwardingOptions.ForwardedHeaders = ForwardedHeaders.All;
            app.UseForwardedHeaders(forwardingOptions);
            app.UseCors();
            app.UsePayServer();
            app.UseStaticFiles();
            app.UseProviderStorage(options);
            app.UseAuthentication();
            app.UseSession();
            app.UseRequestLocalization();
            app.UseSignalR(route =>
            {
                AppHub.Register(route);
                PaymentRequestHub.Register(route);
            });
            app.UseWebSockets();
            app.UseStatusCodePages();
            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
            
            var locOptions = app.ApplicationServices.GetService<IOptions<RequestLocalizationOptions>>();
            app.UseRequestLocalization(locOptions.Value);
        }
    }
}
