namespace BTCPayServer.Events;

public abstract class UserStoreEvent(string storeId, string userId)
{
    public string StoreId { get; } = storeId;
    public string UserId { get; } = userId;

    protected new virtual string ToString()
    {
        return $"StoreUserEvent: User {UserId}, Store {StoreId}";
    }
}
