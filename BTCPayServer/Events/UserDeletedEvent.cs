using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class UserDeletedEvent(ApplicationUser user) : UserEvent(user)
{
    protected override string ToString()
    {
        return $"{base.ToString()} has been deleted";
    }
}

