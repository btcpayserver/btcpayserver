#nullable enable
using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class UserUpdatedEvent(ApplicationUser user, string? detail = null) : UserEvent(user, detail)
{
    protected override string ToString()
    {
        return $"{base.ToString()} has been updated";
    }
}
