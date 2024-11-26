#nullable enable
namespace BTCPayServer.Events;

public abstract class UserStoreEvent(string storeId, string userId, string? detail = null)
{
    public string StoreId { get; } = storeId;
    public string UserId { get; } = userId;
    public string? Detail { get; } = detail;

    protected new virtual string ToString()
    {
        return $"StoreUserEvent: User {UserId}, Store {StoreId}";
    }
}
