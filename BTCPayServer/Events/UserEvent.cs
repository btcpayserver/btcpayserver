#nullable enable
using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class UserEvent(ApplicationUser user, string? detail = null)
{
    public ApplicationUser User { get; } = user;
    public string? Detail { get; } = detail;

    protected new virtual string ToString()
    {
        return $"UserEvent: User \"{User.Email}\" ({User.Id})";
    }
}
