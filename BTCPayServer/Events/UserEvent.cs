using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class UserEvent(ApplicationUser user)
{
    public ApplicationUser User { get; } = user;

    protected new virtual string ToString()
    {
        return $"UserEvent: User \"{user.Email}\" ({user.Id})";
    }
}
