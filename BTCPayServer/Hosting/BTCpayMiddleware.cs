using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
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
                }
                if (isBitpayAPI)
                {
                    await _Next(httpContext);
                    return;
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
