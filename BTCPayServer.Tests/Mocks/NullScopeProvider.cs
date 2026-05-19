#nullable enable
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Tests.Mocks;

/// <summary>Test double: no ambient store scope.</summary>
public sealed class NullScopeProvider : IScopeProvider
{
    public string? GetCurrentStoreId() => null;
}
