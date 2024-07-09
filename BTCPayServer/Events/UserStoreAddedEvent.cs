namespace BTCPayServer.Events;

public class UserStoreAddedEvent(string storeId, string userId) : UserStoreEvent(storeId, userId)
{
    protected override string ToString()
    {
        return $"{base.ToString()} has been added";
    }
}
