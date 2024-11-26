#nullable enable
using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class StoreCreatedEvent(StoreData store, string? detail = null) : StoreEvent(store, detail)
{
    protected override string ToString()
    {
        return $"{base.ToString()} has been created";
    }
}
