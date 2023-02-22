namespace BTCPayServer.Events;

public class StoreRemovedEvent
{
    public StoreRemovedEvent(string storeId)
    {
        StoreId = storeId;
    }
    public string StoreId { get; set; }
    public override string ToString()
    {
        return $"Store {StoreId} has been removed";
    }
}
