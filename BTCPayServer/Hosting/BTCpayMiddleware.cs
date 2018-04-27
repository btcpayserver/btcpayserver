using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using Microsoft.AspNetCore.Http.Internal;
using System.IO;
using BTCPayServer.Authentication;
using System.Security.Principal;
using NBitpayClient.Extensions;
using BTCPayServer.Logging;
using Newtonsoft.Json;
using BTCPayServer.Models;
using BTCPayServer.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Http.Extensions;
using BTCPayServer.Controllers;
using System.Net.WebSockets;
using System.Security.Claims;
using BTCPayServer.Services;
using NBitpayClient;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Hosting
{
    public class BTCPayMiddleware
    {
        TokenRepository _TokenRepository;
        RequestDelegate _Next;
        BTCPayServerOptions _Options;

        public BTCPayMiddleware(RequestDelegate next,
            TokenRepository tokenRepo,
            BTCPayServerOptions options)
        {
            _TokenRepository = tokenRepo ?? throw new ArgumentNullException(nameof(tokenRepo));
            _Next = next ?? throw new ArgumentNullException(nameof(next));
            _Options = options ?? throw new ArgumentNullException(nameof(options));
        }


        public async Task Invoke(HttpContext httpContext)
        {
            RewriteHostIfNeeded(httpContext);
            httpContext.Request.Headers.TryGetValue("x-signature", out StringValues values);
            var sig = values.FirstOrDefault();
            httpContext.Request.Headers.TryGetValue("x-identity", out values);
            var id = values.FirstOrDefault();
            try
            {
                if (!string.IsNullOrEmpty(sig) && !string.IsNullOrEmpty(id))
                {
                    await HandleBitId(httpContext, sig, id);
                }
                await _Next(httpContext);
            }
            catch (WebSocketException)
            { }
            catch (UnauthorizedAccessException ex)
            {
                await HandleBitpayHttpException(httpContext, new BitpayHttpException(401, ex.Message));
            }
            catch (BitpayHttpException ex)
            {
                await HandleBitpayHttpException(httpContext, ex);
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogCritical(new EventId(), ex, "Unhandled exception in BTCPayMiddleware");
                throw;
            }
        }        

        private void RewriteHostIfNeeded(HttpContext httpContext)
        {
            string reverseProxyScheme = null;
            if (httpContext.Request.Headers.TryGetValue("X-Forwarded-Proto", out StringValues proto))
            {
                var scheme = proto.SingleOrDefault();
                if (scheme != null)
                {
                    reverseProxyScheme = scheme;
                }
            }

            ushort? reverseProxyPort = null;
            if (httpContext.Request.Headers.TryGetValue("X-Forwarded-Port", out StringValues port))
            {
                var portString = port.SingleOrDefault();
                if (portString != null && ushort.TryParse(portString, out ushort pp))
                {
                    reverseProxyPort = pp;
                }
            }

            // Make sure that code executing after this point think that the external url has been hit.
            if (_Options.ExternalUrl != null)
            {
                if (reverseProxyScheme != null && _Options.ExternalUrl.Scheme != reverseProxyScheme)
                {
                    if (reverseProxyScheme == "http" && _Options.ExternalUrl.Scheme == "https")
                        Logs.PayServer.LogWarning($"BTCPay ExternalUrl setting expected to use scheme '{_Options.ExternalUrl.Scheme}' externally, but the reverse proxy uses scheme '{reverseProxyScheme}'");
                    httpContext.Request.Scheme = reverseProxyScheme;
                }
                else
                {
                    httpContext.Request.Scheme = _Options.ExternalUrl.Scheme;
                }
                if (_Options.ExternalUrl.IsDefaultPort)
                    httpContext.Request.Host = new HostString(_Options.ExternalUrl.Host);
                else
                {
                    if (reverseProxyPort != null && _Options.ExternalUrl.Port != reverseProxyPort.Value)
                    {
                        Logs.PayServer.LogWarning($"BTCPay ExternalUrl setting expected to use port '{_Options.ExternalUrl.Port}' externally, but the reverse proxy uses port '{reverseProxyPort.Value}'");
                        httpContext.Request.Host = new HostString(_Options.ExternalUrl.Host, reverseProxyPort.Value);
                    }
                    else
                    {
                        httpContext.Request.Host = new HostString(_Options.ExternalUrl.Host, _Options.ExternalUrl.Port);
                    }
                }
            }
            // NGINX pass X-Forwarded-Proto and X-Forwarded-Port, so let's use that to have better guess of the real domain
            else
            {
                ushort? p = null;
                if (reverseProxyScheme != null)
                {
                    httpContext.Request.Scheme = reverseProxyScheme;
                    if (reverseProxyScheme == "http")
                        p = 80;
                    if (reverseProxyScheme == "https")
                        p = 443;
                }


                if (reverseProxyPort != null)
                {
                    p = reverseProxyPort.Value;
                }

                if (p.HasValue)
                {
                    bool isDefault = httpContext.Request.Scheme == "http" && p.Value == 80;
                    isDefault |= httpContext.Request.Scheme == "https" && p.Value == 443;
                    if (isDefault)
                        httpContext.Request.Host = new HostString(httpContext.Request.Host.Host);
                    else
                        httpContext.Request.Host = new HostString(httpContext.Request.Host.Host, p.Value);
                }
            }
        }

        private static async Task HandleBitpayHttpException(HttpContext httpContext, BitpayHttpException ex)
        {
            httpContext.Response.StatusCode = ex.StatusCode;
            using (var writer = new StreamWriter(httpContext.Response.Body, new UTF8Encoding(false), 1024, true))
            {
                httpContext.Response.ContentType = "application/json";
                var result = JsonConvert.SerializeObject(new BitpayErrorsModel(ex));
                writer.Write(result);
                await writer.FlushAsync();
            }
        }


        private async Task HandleBitId(HttpContext httpContext, string sig, string id)
        {
            httpContext.Request.EnableRewind();

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
                            throw new BitpayHttpException(401, $"This endpoint does not support this facade");
                        }
                        identity.AddClaim(new Claim(Claims.OwnStore, bitToken.StoreId));
                    }
                    Logs.PayServer.LogDebug($"BitId signature check success for SIN {sin}");
                }
            }
            catch (FormatException) { }
            if (!httpContext.User.HasClaim(c => c.Type == Claims.SIN))
                Logs.PayServer.LogDebug("BitId signature check failed");
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
