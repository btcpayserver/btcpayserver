#nullable enable
using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class StoreRemovedEvent(StoreData store, string? detail = null) : StoreEvent(store, detail)
{
    protected override string ToString()
    {
        return $"{base.ToString()} has been removed";
    }
}
