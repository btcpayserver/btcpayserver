using System;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Configuration;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;

namespace BTCPayServer.Hosting
{
    public class BTCPayMiddleware
    {
        readonly RequestDelegate _Next;
        readonly BTCPayServerOptions _Options;

        public Logs Logs { get; }

        readonly BTCPayServerEnvironment _Env;

        public BTCPayMiddleware(RequestDelegate next,
            BTCPayServerOptions options,
            BTCPayServerEnvironment env,
            Logs logs)
        {
            _Env = env ?? throw new ArgumentNullException(nameof(env));
            _Next = next ?? throw new ArgumentNullException(nameof(next));
            _Options = options ?? throw new ArgumentNullException(nameof(options));
            Logs = logs;
        }


        public async Task Invoke(HttpContext httpContext)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            try
            {
                var bitpayAuth = GetBitpayAuth(httpContext, out bool isBitpayAuth);
                var isBitpayAPI = IsBitpayAPI(httpContext, isBitpayAuth);
                if (isBitpayAPI && httpContext.Request.Method == "OPTIONS")
                {
                    httpContext.Response.StatusCode = 200;
                    httpContext.Response.SetHeader("Access-Control-Allow-Origin", "*");
                    if (httpContext.Request.Headers.ContainsKey("Access-Control-Request-Headers"))
                    {
                        httpContext.Response.SetHeader("Access-Control-Allow-Headers", httpContext.Request.Headers["Access-Control-Request-Headers"].FirstOrDefault());
                    }
                    return; // We bypass MVC completely
                }
                httpContext.SetIsBitpayAPI(isBitpayAPI);
                if (isBitpayAPI)
                {
                    httpContext.Response.SetHeader("Access-Control-Allow-Origin", "*");
                    httpContext.SetBitpayAuth(bitpayAuth);
                    await _Next(httpContext);
                    return;
                }

                var isHtml = httpContext.Request.Headers.TryGetValue("Accept", out var accept)
                            && accept.ToString().StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
                var isModal = httpContext.Request.Query.TryGetValue("view", out var view)
                            && view.ToString().Equals("modal", StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(_Env.OnionUrl) &&
                    !httpContext.Request.IsOnion() &&
                    isHtml &&
                    !isModal)
                {
                    var onionLocation = _Env.OnionUrl + httpContext.Request.GetEncodedPathAndQuery();
                    httpContext.Response.SetHeader("Onion-Location", onionLocation);
                }
            }
            catch (WebSocketException)
            { }
            catch (UnauthorizedAccessException ex)
            {
                await HandleBitpayHttpException(httpContext, new BitpayHttpException(401, ex.Message));
                return;
            }
            catch (BitpayHttpException ex)
            {
                await HandleBitpayHttpException(httpContext, ex);
                return;
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogCritical(new EventId(), ex, "Unhandled exception in BTCPayMiddleware");
                throw;
            }
            await _Next(httpContext);
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

            // In case of anyone can create invoice, the storeId can be set explicitely
            bitpayAuth |= httpContext.Request.Query.ContainsKey("storeid");

            var isJson = (httpContext.Request.ContentType ?? string.Empty).StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
            var path = httpContext.Request.Path.Value;
            var method = httpContext.Request.Method;
            var isCors = method == "OPTIONS";

            if (
                (isCors || bitpayAuth) &&
                (path == "/invoices" || path == "/invoices/") &&
              (isCors || (method == "POST" && isJson)))
                return true;

            if (
                (isCors || bitpayAuth) &&
                 (path == "/invoices" || path == "/invoices/") &&
                 (isCors || method == "GET"))
                return true;

            if (
               path.StartsWith("/invoices/", StringComparison.OrdinalIgnoreCase) &&
               (isCors || method == "GET") &&
               (isCors || isJson || httpContext.Request.Query.ContainsKey("token")))
                return true;

            if (path.StartsWith("/rates", StringComparison.OrdinalIgnoreCase) &&
                (isCors || method == "GET"))
                return true;

            if (
                path.Equals("/tokens", StringComparison.Ordinal) &&
                (isCors || method == "GET" || method == "POST"))
                return true;

            return false;
        }

        private static async Task HandleBitpayHttpException(HttpContext httpContext, BitpayHttpException ex)
        {
            httpContext.Response.StatusCode = ex.StatusCode;
            httpContext.Response.ContentType = "application/json";
            var result = JsonConvert.SerializeObject(new BitpayErrorsModel(ex));
            await httpContext.Response.WriteAsync(result);
        }
    }
}
