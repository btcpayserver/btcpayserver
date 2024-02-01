using System;
using System.Threading.Tasks;
using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class UserRegisteredEvent
{
    public ApplicationUser User { get; set; }
    public bool Admin { get; set; }
    public Uri RequestUri { get; set; }

    public TaskCompletionSource<Uri> CallbackUrlGenerated;
}

public class UserInvitedEvent : UserRegisteredEvent
{
    public ApplicationUser InvitedByUser { get; set; }
}
