using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class UserUpdatedEvent(ApplicationUser user) : UserEvent(user)
{
    protected override string ToString()
    {
        return $"{base.ToString()} has been updated";
    }
}
