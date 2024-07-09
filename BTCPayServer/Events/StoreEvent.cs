using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class StoreEvent(StoreData store)
{
    public string StoreId { get; } = store.Id;

    protected new virtual string ToString()
    {
        return $"StoreEvent: Store \"{store.StoreName}\" ({store.Id})";
    }
}
