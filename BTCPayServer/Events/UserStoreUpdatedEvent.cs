namespace BTCPayServer.Events;

public class UserStoreUpdatedEvent(string storeId, string userId) : UserStoreEvent(storeId, userId)
{
    public override string ToString()
    {
        return $"User {UserId} and store {StoreId} relation has been updated";
    }
}
