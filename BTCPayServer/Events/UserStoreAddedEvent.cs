namespace BTCPayServer.Events;

public class UserStoreAddedEvent(string storeId, string userId) : UserStoreEvent(storeId, userId)
{
    public override string ToString()
    {
        return $"User {UserId} has been added to store {StoreId}";
    }
}
