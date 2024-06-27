namespace BTCPayServer.Events;

public abstract class UserStoreEvent(string storeId, string userId)
{
    public string StoreId { get; } = storeId;
    public string UserId { get; } = userId;
    public new virtual string ToString()
    {
        return $"StoreUserEvent: User {UserId}, Store {StoreId}";
    }
}
