using System;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Http.Extensions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Authentication;
using BTCPayServer.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitpayClient;
using NBitpayClient.Extensions;
using Newtonsoft.Json.Linq;
using BTCPayServer.Logging;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.AspNetCore.Authentication;
using System.Text.Encodings.Web;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Security
{
    public class BitpayAuthentication
    {
        public class BitpayAuthOptions : AuthenticationSchemeOptions
        {

        }
        class BitpayAuthHandler : AuthenticationHandler<BitpayAuthOptions>
        {
            StoreRepository _StoreRepository;
            TokenRepository _TokenRepository;
            public BitpayAuthHandler(
                TokenRepository tokenRepository,
                StoreRepository storeRepository,
                IOptionsMonitor<BitpayAuthOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock) : base(options, logger, encoder, clock)
            {
                _TokenRepository = tokenRepository;
                _StoreRepository = storeRepository;
            }

            protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                if (Context.Request.HttpContext.GetIsBitpayAPI())
                {
                    List<Claim> claims = new List<Claim>();
                    var bitpayAuth = Context.Request.HttpContext.GetBitpayAuth();
                    string storeId = null;
                    // Careful, those are not the opposite. failedAuth says if a the tentative failed.
                    // successAuth, ensure that at least one succeed.
                    var failedAuth = false;
                    var successAuth = false;
                    if (!string.IsNullOrEmpty(bitpayAuth.Signature) && !string.IsNullOrEmpty(bitpayAuth.Id))
                    {
                        var result = await CheckBitId(Context.Request.HttpContext, bitpayAuth.Signature, bitpayAuth.Id, claims);
                        storeId = result.StoreId;
                        failedAuth = !result.SuccessAuth;
                        successAuth = result.SuccessAuth;
                    }
                    else if (!string.IsNullOrEmpty(bitpayAuth.Authorization))
                    {
                        storeId = await CheckLegacyAPIKey(Context.Request.HttpContext, bitpayAuth.Authorization);
                        if (storeId == null)
                        {
                            Logs.PayServer.LogDebug("API key check failed");
                            failedAuth = true;
                        }
                        successAuth = storeId != null;
                    }

                    if (failedAuth)
                    {
                        return AuthenticateResult.Fail("Invalid credentials");
                    }

                    if (successAuth)
                    {
                        if (storeId != null)
                        {
                            claims.Add(new Claim(Policies.CanCreateInvoice.Key, storeId));
                            var store = await _StoreRepository.FindStore(storeId);
                            store.AdditionalClaims.AddRange(claims);
                            Context.Request.HttpContext.SetStoreData(store);
                        }
                        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Policies.BitpayAuthentication)), Policies.BitpayAuthentication));
                    }
                }
                return AuthenticateResult.NoResult();
            }

            private async Task<(string StoreId, bool SuccessAuth)> CheckBitId(HttpContext httpContext, string sig, string id, List<Claim> claims)
            {
                httpContext.Request.EnableRewind();

                string storeId = null;
                string body = string.Empty;
                if (httpContext.Request.ContentLength != 0 && httpContext.Request.Body != null)
                {
                    using (StreamReader reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, true, 1024, true))
                    {
                        body = reader.ReadToEnd();
                    }
                    httpContext.Request.Body.Position = 0;
                }

                var url = httpContext.Request.GetEncodedUrl();
                try
                {
                    var key = new PubKey(id);
                    if (BitIdExtensions.CheckBitIDSignature(key, sig, url, body))
                    {
                        var sin = key.GetBitIDSIN();
                        claims.Add(new Claim(Claims.SIN, sin));

                        string token = null;
                        if (httpContext.Request.Query.TryGetValue("token", out var tokenValues))
                        {
                            token = tokenValues[0];
                        }

                        if (token == null && !String.IsNullOrEmpty(body) && httpContext.Request.Method == "POST")
                        {
                            try
                            {
                                token = JObject.Parse(body)?.Property("token")?.Value?.Value<string>();
                            }
                            catch { }
                        }

                        if (token != null)
                        {
                            var bitToken = await GetTokenPermissionAsync(sin, token);
                            if (bitToken == null)
                            {
                                return (null, false);
                            }
                            storeId = bitToken.StoreId;
                        }
                    }
                }
                catch (FormatException) { }
                return (storeId, true);
            }

            private async Task<string> CheckLegacyAPIKey(HttpContext httpContext, string auth)
            {
                var splitted = auth.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (splitted.Length != 2 || !splitted[0].Equals("Basic", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string apiKey = null;
                try
                {
                    apiKey = Encoders.ASCII.EncodeData(Encoders.Base64.DecodeData(splitted[1]));
                }
                catch
                {
                    return null;
                }
                return await _TokenRepository.GetStoreIdFromAPIKey(apiKey);
            }

            private async Task<BitTokenEntity> GetTokenPermissionAsync(string sin, string expectedToken)
            {
                var actualTokens = (await _TokenRepository.GetTokens(sin)).ToArray();
                actualTokens = actualTokens.SelectMany(t => GetCompatibleTokens(t)).ToArray();

                var actualToken = actualTokens.FirstOrDefault(a => a.Value.Equals(expectedToken, StringComparison.Ordinal));
                if (expectedToken == null || actualToken == null)
                {
                    Logs.PayServer.LogDebug($"No token found for facade {Facade.Merchant} for SIN {sin}");
                    return null;
                }
                return actualToken;
            }

            private IEnumerable<BitTokenEntity> GetCompatibleTokens(BitTokenEntity token)
            {
                if (token.Facade == Facade.Merchant.ToString())
                {
                    yield return token.Clone(Facade.User);
                    yield return token.Clone(Facade.PointOfSale);
                }
                if (token.Facade == Facade.PointOfSale.ToString())
                {
                    yield return token.Clone(Facade.User);
                }
                yield return token;
            }

            private bool IsBitpayAPI(HttpContext httpContext, bool bitpayAuth)
            {
                if (!httpContext.Request.Path.HasValue)
                    return false;

                var isJson = (httpContext.Request.ContentType ?? string.Empty).StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
                var path = httpContext.Request.Path.Value;
                if (
                    bitpayAuth &&
                    (path == "/invoices" || path == "/invoices/") &&
                  httpContext.Request.Method == "POST" &&
                  isJson)
                    return true;

                if (
                    bitpayAuth &&
                    (path == "/invoices" || path == "/invoices/") &&
                  httpContext.Request.Method == "GET")
                    return true;

                if (
                    path.StartsWith("/invoices/", StringComparison.OrdinalIgnoreCase) &&
                    httpContext.Request.Method == "GET" &&
                    (isJson || httpContext.Request.Query.ContainsKey("token")))
                    return true;

                if (path.Equals("/rates", StringComparison.OrdinalIgnoreCase) &&
                    httpContext.Request.Method == "GET")
                    return true;

                if (
                    path.Equals("/tokens", StringComparison.Ordinal) &&
                    (httpContext.Request.Method == "GET" || httpContext.Request.Method == "POST"))
                    return true;

                return false;
            }
        }
        internal static void AddAuthentication(IServiceCollection services, Action<BitpayAuthOptions> bitpayAuth = null)
        {
            bitpayAuth = bitpayAuth ?? new Action<BitpayAuthOptions>((o) => { });
            services.AddAuthentication().AddScheme<BitpayAuthOptions, BitpayAuthHandler>(Policies.BitpayAuthentication, bitpayAuth);
        }
    }
}
