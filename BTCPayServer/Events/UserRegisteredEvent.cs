using System;
using System.Threading.Tasks;
using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class UserRegisteredEvent
{
    public ApplicationUser User { get; set; }
    public bool Admin { get; set; }
    public UserRegisteredEventKind Kind { get; set; } = UserRegisteredEventKind.Registration;
    public Uri RequestUri { get; set; }
    public ApplicationUser InvitedByUser { get; set; }
    public bool SendInvitationEmail { get; set; }
    public TaskCompletionSource<Uri> CallbackUrlGenerated;
}

public enum UserRegisteredEventKind
{
    Registration,
    Invite
}
