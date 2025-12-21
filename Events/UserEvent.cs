#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Http;

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
    public class PasswordResetRequested(ApplicationUser user, string resetLink) : UserEvent(user)
    {
        public string ResetLink { get; } = resetLink;
    }

    public class ConfirmationEmailRequested(ApplicationUser user, string confirmLink) : UserEvent(user)
    {
        public string ConfirmLink { get; } = confirmLink;
    }

    public class Registered(ApplicationUser user, RequestBaseUrl requestBaseUrl, string approvalLink, string confirmationEmail) : UserEvent(user)
    {
        public string ApprovalLink { get; } = approvalLink;
		public string ConfirmationEmailLink { get; set; } = confirmationEmail;
        public RequestBaseUrl RequestBaseUrl { get; set; } = requestBaseUrl;
		public static async Task<Registered> Create(ApplicationUser user, ApplicationUser? invitedBy, CallbackGenerator callbackGenerator, bool sendInvitationEmail = true)
		{
			var approvalLink = callbackGenerator.ForApproval(user);
			var confirmationEmail = await callbackGenerator.ForEmailConfirmation(user);
            if (invitedBy is null)
                return new Registered(user, callbackGenerator.GetRequestBaseUrl(), approvalLink, confirmationEmail);
            var invitationLink = await callbackGenerator.ForInvitation(user);
            return new Invited(user, invitedBy, callbackGenerator.GetRequestBaseUrl(), invitationLink, approvalLink, confirmationEmail)
            {
                SendInvitationEmail = sendInvitationEmail
            };
		}
	}
    public class Invited(ApplicationUser user, ApplicationUser invitedBy, RequestBaseUrl requestBaseUrl, string invitationLink, string approvalLink, string confirmationEmail) : Registered(user, requestBaseUrl, approvalLink, confirmationEmail)
    {
        public bool SendInvitationEmail { get; set; }
        public ApplicationUser InvitedByUser { get; } = invitedBy;
        public string InvitationLink { get; } = invitationLink;
    }
    public class Updated(ApplicationUser user) : UserEvent(user)
    {
        protected override string ToString()
        {
            return $"{base.ToString()} has been updated";
        }
    }
    public class Approved(ApplicationUser user, string loginLink) : UserEvent(user)
    {
        public string LoginLink { get; set; } = loginLink;
        protected override string ToString()
        {
            return $"{base.ToString()} has been approved";
        }
    }

    public class ConfirmedEmail(ApplicationUser user, string approvalLink): UserEvent(user)
    {
		public string ApprovalLink { get; set; } = approvalLink;

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
