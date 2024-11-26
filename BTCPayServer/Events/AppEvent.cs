#nullable enable
using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class AppEvent(AppData app, string? detail = null)
{
    public string AppId { get; } = app.Id;
    public string StoreId { get; } = app.StoreDataId;
    public string? Detail { get; } = detail;

    protected new virtual string ToString()
    {
        return $"AppEvent: App \"{app.Name}\" ({StoreId})";
    }
}
