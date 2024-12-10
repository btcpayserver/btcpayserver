namespace BTCPayServer.Events;

public class UserStoreRemovedEvent(string storeId, string userId) : UserStoreEvent(storeId, userId)
{
    protected override string ToString()
    {
        return $"{base.ToString()} has been removed";
    }
}
