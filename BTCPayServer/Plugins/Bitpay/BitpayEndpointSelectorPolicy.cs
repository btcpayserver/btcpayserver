#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;

namespace BTCPayServer.Plugins.Bitpay;

public class BitpayEndpointSelectorPolicy : MatcherPolicy, IEndpointSelectorPolicy
{
    public class BitpayEndpointMetadata : Attribute;

    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints)
    => endpoints.Any(e => e.Metadata.GetMetadata<BitpayEndpointMetadata>() is not null);

    public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
    {
        var isBitpayAuth = httpContext.TryGetBitpayAuth(out _);
        var isBitpayAPI = IsBitpayAPI(httpContext, isBitpayAuth);
        for (var i = 0; i < candidates.Count; i++)
        {
            var bitpayEndpoint = candidates[i].Endpoint.Metadata.GetMetadata<BitpayEndpointMetadata>();
            candidates.SetValidity(i, bitpayEndpoint is not null == isBitpayAPI);
        }
        return Task.CompletedTask;
    }

    private bool IsBitpayAPI(HttpContext httpContext, bool bitpayAuth)
    {
        if (!httpContext.Request.Path.HasValue)
            return false;

        // In case of anyone can create an invoice, the storeId can be set explicitly
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
            path.Equals("/tokens", StringComparison.OrdinalIgnoreCase) &&
            (isCors || method == "GET" || method == "POST"))
            return true;

        return false;
    }

    public override int Order { get; } = 100;
}
