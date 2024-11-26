#nullable enable
namespace BTCPayServer.Events;

public class UserStoreUpdatedEvent(string storeId, string userId, string? detail = null) : UserStoreEvent(storeId, userId, detail)
{
    protected override string ToString()
    {
        return $"{base.ToString()} has been updated";
    }
}
