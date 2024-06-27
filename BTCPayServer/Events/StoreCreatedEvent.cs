using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class StoreCreatedEvent(StoreData store)
{
    public StoreData Store { get; } = store;

    public override string ToString()
    {
        return $"Store \"{Store.StoreName}\" has been created";
    }
}
