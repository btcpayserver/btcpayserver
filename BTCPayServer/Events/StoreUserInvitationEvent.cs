#nullable enable

using BTCPayServer.Data;

namespace BTCPayServer.Events;

public abstract class StoreUserInvitationEvent(StoreInvitationRow invitation)
{
    public StoreInvitationRow Invitation { get; } = invitation;

    public class Created(StoreInvitationRow invitation, string? invitationsLink)
        : StoreUserInvitationEvent(invitation)
    {
        public string? InvitationsLink { get; } = invitationsLink;
        protected override string ToString() => $"{base.ToString()} has been invited";
    }

    public class Accepted(StoreInvitationRow invitation) : StoreUserInvitationEvent(invitation)
    {
        protected override string ToString() => $"{base.ToString()} accepted the invitation";
    }

    protected new virtual string ToString() => $"StoreUserInvitationEvent: User {Invitation.UserId}, Store {Invitation.StoreId}";
}
