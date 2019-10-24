using System;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http.Extensions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
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

using Microsoft.AspNetCore.Authentication;
using System.Text.Encodings.Web;
using BTCPayServer.Data;


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
            if (!Context.Request.HttpContext.TryGetBitpayAuth(out var bitpayAuth))
                return AuthenticateResult.NoResult();
            if (!string.IsNullOrEmpty(bitpayAuth.Signature) && !string.IsNullOrEmpty(bitpayAuth.Id))
            {
                var sin = await CheckBitId(Context.Request.HttpContext, bitpayAuth.Signature, bitpayAuth.Id);
                if (sin == null)
                    return AuthenticateResult.Fail("BitId authentication failed");
                return Success(BitpayClaims.SIN, sin, BitpayAuthenticationTypes.SinAuthentication);
            }
            else if (!string.IsNullOrEmpty(bitpayAuth.Authorization))
            {
                var storeId = await GetStoreIdFromAuth(Context.Request.HttpContext, bitpayAuth.Authorization);
                if (storeId == null)
                    return AuthenticateResult.Fail("ApiKey authentication failed");
                return Success(BitpayClaims.ApiKeyStoreId, storeId, BitpayAuthenticationTypes.ApiKeyAuthentication);
            }
            else
            {
                return Success(null, null, BitpayAuthenticationTypes.Anonymous);
            }
        }

        private AuthenticateResult Success(string claimType, string claimValue, string authenticationType)
        {
            List<Claim> claims = new List<Claim>();
            if (claimType != null)
                claims.Add(new Claim(claimType, claimValue));
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType)), authenticationType));
        }

        private async Task<string> CheckBitId(HttpContext httpContext, string sig, string id)
        {
            httpContext.Request.EnableBuffering();
            string body = string.Empty;
            if (httpContext.Request.ContentLength != 0 && httpContext.Request.Body != null)
            {
                using (StreamReader reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, true, 1024, true))
                {
                    body = await reader.ReadToEndAsync();
                }
                httpContext.Request.Body.Position = 0;
            }

            var url = httpContext.Request.GetEncodedUrl();
            try
            {
                var key = new PubKey(id);
                if (BitIdExtensions.CheckBitIDSignature(key, sig, url, body))
                {
                    return key.GetBitIDSIN();
                }
            }
            catch { }
            return null;
        }

        private async Task<string> GetStoreIdFromAuth(HttpContext httpContext, string auth)
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
