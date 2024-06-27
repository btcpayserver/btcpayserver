namespace BTCPayServer.Events;

public class StoreRemovedEvent(string storeId)
{
    public string StoreId { get; } = storeId;

    public override string ToString()
    {
        return $"Store {StoreId} has been removed";
    }
}
