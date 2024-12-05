#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class UserEvent(ApplicationUser user)
{
    public class Deleted(ApplicationUser user) : UserEvent(user)
    {
        protected override string ToString()
        {
            return $"{base.ToString()} has been deleted";
        }
    }
    public class InviteAccepted(ApplicationUser user, Uri requestUri) : UserEvent(user)
    {
        public Uri RequestUri { get; } = requestUri;
    }
    public class PasswordResetRequested(ApplicationUser user, Uri requestUri) : UserEvent(user)
    {
        public Uri RequestUri { get; } = requestUri;
        public TaskCompletionSource<Uri>? CallbackUrlGenerated;
    }
    public class Registered(ApplicationUser user, Uri requestUri) : UserEvent(user)
    {
        public bool Admin { get; set; }
        public Uri RequestUri { get; set; } = requestUri;
        public TaskCompletionSource<Uri>? CallbackUrlGenerated;
    }
    public class Invited(ApplicationUser user, ApplicationUser invitedBy, Uri requestUri) : Registered(user, requestUri)
    {
        public bool SendInvitationEmail { get; set; } = true;
        public ApplicationUser InvitedByUser { get; } = invitedBy;
    }
    public class Updated(ApplicationUser user) : UserEvent(user)
    {
        protected override string ToString()
        {
            return $"{base.ToString()} has been updated";
        }
    }
    public class Approved(ApplicationUser user, Uri requestUri) : UserEvent(user)
    {
        public Uri RequestUri { get; set; } = requestUri;
        protected override string ToString()
        {
            return $"{base.ToString()} has been approved";
        }
    }

    public class ConfirmedEmail(ApplicationUser user, Uri requestUri): UserEvent(user)
    {
        public Uri RequestUri { get; } = requestUri;
        protected override string ToString()
        {
            return $"{base.ToString()} has email confirmed";
        }
    }

    public ApplicationUser User { get; } = user;

    protected new virtual string ToString()
    {
        return $"UserEvent: User \"{User.Email}\" ({User.Id})";
    }
}
