using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace BTCPayServer.AuthorizationCode.CSharp.AspNetCore
{
    public class Startup
    {
        //This app uses the Code Flow and authenticates against a BTCPay's instance userbase.
        public void ConfigureServices(IServiceCollection services)
        {
             services.AddAuthentication(options =>
                        {
                            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                        })
            
                        .AddCookie(options =>
                        {
                            options.LoginPath = new PathString("/signin");
                        })
            
                        .AddOpenIdConnect(options =>
                        {
                            // Note: these settings must match the application details
                            // inserted in the database at the server level.
                            options.ClientId = "mvc";
                            options.ClientSecret = "901564A5-E7FE-42CB-B10D-61EF6A8F3654";
                            // Note: setting the Authority allows the OIDC client middleware to automatically
                            // retrieve the identity provider's configuration and spare you from setting
                            // the different endpoints URIs or the token validation parameters explicitly.
                            options.Authority = "http://localhost:54540/";
                            
                            options.RequireHttpsMetadata = false;
                            options.GetClaimsFromUserInfoEndpoint = true;
                            options.SaveTokens = true;
                            
                            // Use the authorization code flow.
                            options.ResponseType = OpenIdConnectResponseType.Code;
                            options.AuthenticationMethod = OpenIdConnectRedirectBehavior.RedirectGet;
            
                            
            
                            options.SecurityTokenValidator = new JwtSecurityTokenHandler
                            {
                                // Disable the built-in JWT claims mapping feature.
                                InboundClaimTypeMap = new Dictionary<string, string>()
                            };
            
                            options.TokenValidationParameters.NameClaimType = "name";
                            options.TokenValidationParameters.RoleClaimType = "role";
                        });
            
                        services.AddMvc();
            
                        services.AddSingleton<HttpClient>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseDeveloperExceptionPage();

            app.UseStaticFiles();

            app.UseAuthentication();

            app.UseMvc();
        }
    }
}
