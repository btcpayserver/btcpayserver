namespace BTCPayServer.Events;

public class UserStoreRemovedEvent(string storeId, string userId) : UserStoreEvent(storeId, userId)
{
    public override string ToString()
    {
        return $"User {UserId} has been removed from store {StoreId}";
    }
}
