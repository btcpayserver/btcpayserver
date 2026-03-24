#nullable enable
using BTCPayServer.Abstractions.Contracts;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Services.Stores;

public class ScopeProvider(IHttpContextAccessor httpContextAccessor) : IScopeProvider
{
    public string? GetCurrentStoreId() => httpContextAccessor.HttpContext?.GetCurrentStoreId();
}
