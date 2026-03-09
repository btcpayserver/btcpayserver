#nullable enable
using System.Linq;
using System.Security.Claims;
using BTCPayServer.Plugins.Bitpay.Security;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Plugins.Bitpay;

public static class BitpayExtensions
{

    public static string? GetSIN(this ClaimsPrincipal principal)
        => principal.Claims.Where(c => c.Type == BitpayClaims.SIN).Select(c => c.Value).FirstOrDefault();

    public static bool TryGetBitpayAuth(this HttpContext httpContext, out (string? Signature, string? Id, string? Authorization) result)
    {
        httpContext.Request.Headers.TryGetValue("x-signature", out var values);
        var sig = values.FirstOrDefault();
        httpContext.Request.Headers.TryGetValue("x-identity", out values);
        var id = values.FirstOrDefault();
        httpContext.Request.Headers.TryGetValue("Authorization", out values);
        var auth = values.FirstOrDefault();
        var hasBitpayAuth = auth != null || (sig != null && id != null);
        result = hasBitpayAuth ? (sig, id, auth) : default;
        return hasBitpayAuth;
    }
}
