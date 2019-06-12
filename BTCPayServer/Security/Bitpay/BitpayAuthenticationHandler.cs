using System;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http.Extensions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Authentication;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http;
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


namespace BTCPayServer.Security.Bitpay
{
    public class BitpayAuthenticationHandler : AuthenticationHandler<BitpayAuthenticationOptions>
    {
        StoreRepository _StoreRepository;
        TokenRepository _TokenRepository;
        public BitpayAuthenticationHandler(
            TokenRepository tokenRepository,
            StoreRepository storeRepository,
            IOptionsMonitor<BitpayAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock) : base(options, logger, encoder, clock)
        {
            _TokenRepository = tokenRepository;
            _StoreRepository = storeRepository;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            List<Claim> claims = new List<Claim>();
            if (!Context.Request.HttpContext.TryGetBitpayAuth(out var bitpayAuth))
                return AuthenticateResult.NoResult();
            string storeId = null;
            bool anonymous = true;
            bool? success = null;
            if (!string.IsNullOrEmpty(bitpayAuth.Signature) && !string.IsNullOrEmpty(bitpayAuth.Id))
            {
                var result = await CheckBitId(Context.Request.HttpContext, bitpayAuth.Signature, bitpayAuth.Id, claims);
                storeId = result.StoreId;
                success = result.SuccessAuth;
                anonymous = false;
            }
            else if (!string.IsNullOrEmpty(bitpayAuth.Authorization))
            {
                storeId = await CheckLegacyAPIKey(Context.Request.HttpContext, bitpayAuth.Authorization);
                success = storeId != null;
                anonymous = false;
            }
            else
            {
                if (Context.Request.HttpContext.Request.Query.TryGetValue("storeId", out var storeIdStringValues))
                {
                    storeId = storeIdStringValues.FirstOrDefault() ?? string.Empty;
                    success = true;
                    anonymous = true;
                }
            }

            if (success is true)
            {
                if (storeId != null)
                {
                    claims.Add(new Claim(Policies.CanCreateInvoice.Key, storeId));
                    var store = await _StoreRepository.FindStore(storeId);
                    if (store == null ||
                       (anonymous && !store.GetStoreBlob().AnyoneCanInvoice))
                    {
                        return AuthenticateResult.Fail("Invalid credentials");
                    }
                    store.AdditionalClaims.AddRange(claims);
                    Context.Request.HttpContext.SetStoreData(store);
                }
                return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Policies.BitpayAuthentication)), Policies.BitpayAuthentication));
            }
            else if (success is false)
            {
                return AuthenticateResult.Fail("Invalid credentials");
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
                            token = JObject.Parse(body)?.Property("token", StringComparison.OrdinalIgnoreCase)?.Value?.Value<string>();
                        }
                        catch { }
                    }

                    if (token != null)
                    {
                        var bitToken = (await _TokenRepository.GetTokens(sin)).FirstOrDefault();
                        if (bitToken == null)
                        {
                            return (null, false);
                        }
                        storeId = bitToken.StoreId;
                    }
                }
                else
                {
                    return (storeId, false);
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
    }
}
