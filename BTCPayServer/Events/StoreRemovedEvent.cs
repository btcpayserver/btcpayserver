using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class StoreRemovedEvent(StoreData store) : StoreEvent(store)
{
    protected override string ToString()
    {
        return $"{base.ToString()} has been removed";
    }
}
