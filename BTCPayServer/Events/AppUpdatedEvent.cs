#nullable enable
using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class AppUpdatedEvent(AppData app, string? detail = null) : AppEvent(app, detail ?? app.AppType)
{
    protected override string ToString()
    {
        return $"{base.ToString()} has been updated";
    }
}
