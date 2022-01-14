using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Security.Greenfield;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace BTCPayServer.Hosting
{
    public class GreenfieldMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IOptions<MvcNewtonsoftJsonOptions> _mvcOptions;

        public GreenfieldMiddleware(RequestDelegate next, IOptions<MvcNewtonsoftJsonOptions> mvcOptions)
        {
            _next = next;
            _mvcOptions = mvcOptions;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            await _next(httpContext);
            if (!httpContext.Response.HasStarted &&
                !IsJson(httpContext.Response.ContentType) &&
                !IsHtml(httpContext.Response.ContentType) &&
                !httpContext.GetIsBitpayAPI() &&
                (httpContext.Response.StatusCode == 401 || httpContext.Response.StatusCode == 403))
            {
                if (httpContext.Response.StatusCode == 403 &&
                    httpContext.Items.TryGetValue(GreenfieldAuthorizationHandler.RequestedPermissionKey, out var p) &&
                    p is string policy)
                {
                    var outputObj = new GreenfieldPermissionAPIError(policy);
                    await WriteError(httpContext, outputObj);
                }
                if (httpContext.Response.StatusCode == 401)
                {
                    var outputObj = new GreenfieldAPIError("unauthenticated", "Authentication is required for accessing this endpoint");
                    await WriteError(httpContext, outputObj);
                }
            }
        }

        private async Task WriteError(HttpContext httpContext, object outputObj)
        {
            string output = JsonConvert.SerializeObject(outputObj, _mvcOptions.Value.SerializerSettings);
            var outputBytes = new UTF8Encoding(false).GetBytes(output);
            httpContext.Response.Headers.Add("Content-Type", "application/json");
            httpContext.Response.Headers.Add("Content-Length", outputBytes.Length.ToString(CultureInfo.InvariantCulture));
            await httpContext.Response.Body.WriteAsync(outputBytes, 0, outputBytes.Length);
        }
        private bool IsHtml(string contentType)
        {
            return contentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) is true;
        }
        private bool IsJson(string contentType)
        {
            return contentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) is true;
        }
    }
}
