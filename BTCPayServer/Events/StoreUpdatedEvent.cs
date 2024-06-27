using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class StoreUpdatedEvent(StoreData store)
{
    public StoreData Store { get; } = store;

    public override string ToString()
    {
        return $"Store \"{Store.StoreName}\" has been updated";
    }
}
