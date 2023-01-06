#nullable enable
namespace BTCPayServer.Abstractions.Contracts;

public interface IScopeProvider
{
    string? GetCurrentStoreId();
}
