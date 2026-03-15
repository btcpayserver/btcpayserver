#nullable enable
using System;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Security;

public static class SecurityExtensions
{
    [Obsolete("Use GetCurrentStoreId instead")]
    public static string? GetImplicitStoreId(this HttpContext httpContext)
        => httpContext.GetCurrentStoreId();
}
