namespace BTCPayServer.Events;

public class UserStoreAddedEvent(string storeId, string userId, string? detail = null) : UserStoreEvent(storeId, userId, detail)
{
    protected override string ToString()
    {
        return $"{base.ToString()} has been added";
    }
}
