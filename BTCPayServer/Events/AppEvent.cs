#nullable enable
using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class AppEvent(AppData app, string? detail = null)
{
    public class Created(AppData app, string? detail = null) : AppEvent(app, detail ?? app.AppType)
    {
        protected override string ToString()
        {
            return $"{base.ToString()} has been created";
        }
    }
    public class Deleted(AppData app, string? detail = null) : AppEvent(app, detail ?? app.AppType)
    {
        protected override string ToString()
        {
            return $"{base.ToString()} has been deleted";
        }
    }
    public class Updated(AppData app, string? detail = null) : AppEvent(app, detail ?? app.AppType)
    {
        protected override string ToString()
        {
            return $"{base.ToString()} has been updated";
        }
    }
    public string AppId { get; } = app.Id;
    public string StoreId { get; } = app.StoreDataId;
    public string? Detail { get; } = detail;

    protected new virtual string ToString()
    {
        return $"AppEvent: App \"{app.Name}\" ({StoreId})";
    }
}
