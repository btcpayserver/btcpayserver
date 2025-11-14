#nullable enable
using System;
using System.Threading.Tasks;
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

    public class Registered(ApplicationUser user, string approvalLink, string confirmationEmail) : UserEvent(user)
    {
        public string ApprovalLink { get; } = approvalLink;
		public string ConfirmationEmailLink { get; set; } = confirmationEmail;
		public static async Task<Registered> Create(ApplicationUser user, CallbackGenerator callbackGenerator)
		{
			var approvalLink = callbackGenerator.ForApproval(user);
			var confirmationEmail = await callbackGenerator.ForEmailConfirmation(user);
			return new Registered(user, approvalLink, confirmationEmail);
		}
	}
    public class Invited(ApplicationUser user, ApplicationUser invitedBy, string invitationLink, string approvalLink, string confirmationEmail) : Registered(user, approvalLink, confirmationEmail)
    {
        public bool SendInvitationEmail { get; set; }
        public ApplicationUser InvitedByUser { get; } = invitedBy;
        public string InvitationLink { get; } = invitationLink;

        public static async Task<Invited> Create(ApplicationUser user, ApplicationUser currentUser, CallbackGenerator callbackGenerator, bool sendEmail)
        {
			var invitationLink = await callbackGenerator.ForInvitation(user);
			var approvalLink = callbackGenerator.ForApproval(user);
			var confirmationEmail = await callbackGenerator.ForEmailConfirmation(user);
			return new Invited(user, currentUser, invitationLink, approvalLink, confirmationEmail)
            {
                SendInvitationEmail = sendEmail
            };
		}
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
