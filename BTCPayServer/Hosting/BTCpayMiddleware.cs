using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using BTCPayServer.Authentication;
using BTCPayServer.Logging;
using Newtonsoft.Json;
using BTCPayServer.Models;
using BTCPayServer.Configuration;
using System.Net.WebSockets;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Hosting
{
    public class BTCPayMiddleware
    {
        RequestDelegate _Next;
        BTCPayServerOptions _Options;

        public BTCPayMiddleware(RequestDelegate next,
            BTCPayServerOptions options)
        {
            _Next = next ?? throw new ArgumentNullException(nameof(next));
            _Options = options ?? throw new ArgumentNullException(nameof(options));
        }


        public async Task Invoke(HttpContext httpContext)
        {
            RewriteHostIfNeeded(httpContext);

            try
            {
                var bitpayAuth = GetBitpayAuth(httpContext, out bool isBitpayAuth);
                var isBitpayAPI = IsBitpayAPI(httpContext, isBitpayAuth);
                httpContext.SetIsBitpayAPI(isBitpayAPI);
                if (isBitpayAPI)
                {
                    httpContext.SetBitpayAuth(bitpayAuth);
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

        private static (string Signature, String Id, String Authorization) GetBitpayAuth(HttpContext httpContext, out bool hasBitpayAuth)
        {
            httpContext.Request.Headers.TryGetValue("x-signature", out StringValues values);
            var sig = values.FirstOrDefault();
            httpContext.Request.Headers.TryGetValue("x-identity", out values);
            var id = values.FirstOrDefault();
            httpContext.Request.Headers.TryGetValue("Authorization", out values);
            var auth = values.FirstOrDefault();
            hasBitpayAuth = auth != null || (sig != null && id != null);
            return (sig, id, auth);
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

            if (path.StartsWith("/rates", StringComparison.OrdinalIgnoreCase) &&
                httpContext.Request.Method == "GET")
                return true;

            if (
                path.Equals("/tokens", StringComparison.Ordinal) &&
                (httpContext.Request.Method == "GET" || httpContext.Request.Method == "POST"))
                return true;

            return false;
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
                        Logs.PayServer.LogWarning($"BTCPay ExternalUrl setting expected to use scheme '{_Options.ExternalUrl.Scheme}' externally, but the reverse proxy uses scheme '{reverseProxyScheme}' (X-Forwarded-Port), forcing ExternalUrl");
                }
                httpContext.Request.Scheme = _Options.ExternalUrl.Scheme;
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
    }
}
