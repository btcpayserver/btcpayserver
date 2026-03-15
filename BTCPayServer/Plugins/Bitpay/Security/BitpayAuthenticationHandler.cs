using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Bitpay.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitpayClient.Extensions;
using Newtonsoft.Json;


namespace BTCPayServer.Plugins.Bitpay.Security
{
    public class BitpayAuthenticationHandler(
        TokenRepository tokenRepository,
        IOptionsMonitor<BitpayAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<BitpayAuthenticationOptions>(options, logger, encoder)
    {
        const string BitpayAuthErrorKey = nameof(BitpayAuthErrorKey);
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            var reason = Context.Items[BitpayAuthErrorKey]?.ToString() ?? "Authentication required";
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.ContentType = "application/json";
            return Response.WriteAsync(JsonConvert.SerializeObject(new BitpayErrorsModel()
            {
                Errors = [new() { Error = reason }],
                Error = reason
            }));
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            Context.Request.HttpContext.TryGetBitpayAuth(out var bitpayAuth);
            if (!string.IsNullOrEmpty(bitpayAuth.Signature) && !string.IsNullOrEmpty(bitpayAuth.Id))
            {
                var sin = await CheckBitId(Context.Request.HttpContext, bitpayAuth.Signature, bitpayAuth.Id);
                if (sin == null)
                    return Fail("BitId authentication failed");
                return Success(BitpayClaims.SIN, sin, BitpayAuthenticationTypes.SinAuthentication);
            }
            else if (!string.IsNullOrEmpty(bitpayAuth.Authorization))
            {
                var storeId = await GetStoreIdFromAuth(bitpayAuth.Authorization);
                if (storeId == null)
                    return Fail("ApiKey authentication failed");
                return Success(BitpayClaims.ApiKeyStoreId, storeId, BitpayAuthenticationTypes.ApiKeyAuthentication);
            }
            else
            {
                return Success(null, null, BitpayAuthenticationTypes.Anonymous);
            }
        }

        private AuthenticateResult Fail(string reason)
        {
            Context.Items[BitpayAuthErrorKey] = reason;
            return AuthenticateResult.Fail(reason);
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

        private async Task<string> GetStoreIdFromAuth(string auth)
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
            return await tokenRepository.GetStoreIdFromAPIKey(apiKey);
        }
    }
}
