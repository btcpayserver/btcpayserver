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

namespace BTCPayServer.Security
{
    public class BitpayClaimsFilter : IAsyncAuthorizationFilter, IConfigureOptions<MvcOptions>
    {
        UserManager<ApplicationUser> _UserManager;
        StoreRepository _StoreRepository;
        TokenRepository _TokenRepository;

        public BitpayClaimsFilter(
            UserManager<ApplicationUser> userManager,
            TokenRepository tokenRepository,
            StoreRepository storeRepository)
        {
            _UserManager = userManager;
            _StoreRepository = storeRepository;
            _TokenRepository = tokenRepository;
        }

        void IConfigureOptions<MvcOptions>.Configure(MvcOptions options)
        {
            options.Filters.Add(typeof(BitpayClaimsFilter));
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var principal = context.HttpContext.User;
            if (context.HttpContext.GetIsBitpayAPI())
            {
                var bitpayAuth = context.HttpContext.GetBitpayAuth();
                string storeId = null;
                var failedAuth = false;
                if (!string.IsNullOrEmpty(bitpayAuth.Signature) && !string.IsNullOrEmpty(bitpayAuth.Id))
                {
                    storeId = await CheckBitId(context.HttpContext, bitpayAuth.Signature, bitpayAuth.Id);
                    if (!context.HttpContext.User.Claims.Any(c => c.Type == Claims.SIN))
                    {
                        Logs.PayServer.LogDebug("BitId signature check failed");
                        failedAuth = true;
                    }
                }
                else if (!string.IsNullOrEmpty(bitpayAuth.Authorization))
                {
                    storeId = await CheckLegacyAPIKey(context.HttpContext, bitpayAuth.Authorization);
                    if (storeId == null)
                    {
                        Logs.PayServer.LogDebug("API key check failed");
                        failedAuth = true;
                    }
                }

                if (storeId != null)
                {
                    var identity = ((ClaimsIdentity)context.HttpContext.User.Identity);
                    identity.AddClaim(new Claim(Policies.CanUseStore.Key, storeId));
                    var store = await _StoreRepository.FindStore(storeId);
                    context.HttpContext.SetStoreData(store);
                }
                else if (failedAuth)
                {
                    throw new BitpayHttpException(401, "Invalid credentials");
                }
            }
        }

        private async Task<string> CheckBitId(HttpContext httpContext, string sig, string id)
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
                    var identity = ((ClaimsIdentity)httpContext.User.Identity);
                    identity.AddClaim(new Claim(Claims.SIN, sin));

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
                            return null;
                        }
                        storeId = bitToken.StoreId;
                    }
                }
            }
            catch (FormatException) { }
            return storeId;
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
    }
}
