using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using NBitpayClient.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace BTCPayServer.Plugins.Bitpay.Security
{
    public class BitpayAuthenticationHandler(
        IOptionsMonitor<BitpayAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<BitpayAuthenticationOptions>(options, logger, encoder)
    {
        const string BitpayAuthErrorKey = nameof(BitpayAuthErrorKey);
        // Typed long so EnableBuffering binds the bufferLimit overload, not bufferThreshold(int) which would leave the body unbounded.
        const long MaxBitIdBodySize = 1_000_000;
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
                var claims = await CheckBitId(Context.Request.HttpContext, bitpayAuth.Signature, bitpayAuth.Id);
                if (claims == null)
                    return Fail("BitId authentication failed");
                return Success(claims, BitpayAuthenticationTypes.SinAuthentication);
            }
            else
            {
                return Success(new List<Claim>(), BitpayAuthenticationTypes.Anonymous);
            }
        }

        private AuthenticateResult Fail(string reason)
        {
            Context.Items[BitpayAuthErrorKey] = reason;
            return AuthenticateResult.Fail(reason);
        }

        private AuthenticateResult Success(List<Claim> claims, string authenticationType)
        {
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType)), authenticationType));
        }

        private async Task<List<Claim>> CheckBitId(HttpContext httpContext, string sig, string id)
        {
            if (httpContext.Request.ContentLength > MaxBitIdBodySize)
                return null;
            httpContext.Request.EnableBuffering(bufferLimit: MaxBitIdBodySize);
            string body = string.Empty;
            if (httpContext.Request.ContentLength != 0 && httpContext.Request.Body != null)
            {
                try
                {
                    using (StreamReader reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, true, 1024, true))
                    {
                        body = await reader.ReadToEndAsync();
                    }
                }
                catch (IOException)
                {
                    return null;
                }
                httpContext.Request.Body.Position = 0;
            }

            var url = httpContext.Request.GetEncodedUrl();
            try
            {
                var key = new PubKey(id);
                if (BitIdExtensions.CheckBitIDSignature(key, sig, url, body))
                {
                    List<Claim> claims = new List<Claim>();
                    claims.Add(new Claim(BitpayClaims.SIN, key.GetBitIDSIN()));
                    if (GetToken(httpContext.Request, body) is { } token)
                        claims.Add(new Claim(BitpayClaims.Token, token));
                    return claims;
                }
            }
            catch { }
            return null;
        }

        private static string GetToken(HttpRequest request, string body)
        {
            var token = request.Query["token"].FirstOrDefault();
            if (string.IsNullOrEmpty(token) && HttpMethods.IsPost(request.Method) && !string.IsNullOrEmpty(body))
            {
                try
                {
                    token = JObject.Parse(body).GetValue("token", StringComparison.OrdinalIgnoreCase)?.Value<string>();
                }
                catch { }
            }
            return string.IsNullOrEmpty(token) ? null : token;
        }
    }
}
