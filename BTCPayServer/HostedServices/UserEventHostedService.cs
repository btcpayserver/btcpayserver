using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices;

public class UserEventHostedService(
    EventAggregator eventAggregator,
    UserManager<ApplicationUser> userManager,
    CallbackGenerator callbackGenerator,
    EmailSenderFactory emailSenderFactory,
    NotificationSender notificationSender,
    StoreRepository storeRepository,
    Logs logs)
    : EventHostedServiceBase(eventAggregator, logs)
{
    public UserManager<ApplicationUser> UserManager { get; } = userManager;
    public CallbackGenerator CallbackGenerator { get; } = callbackGenerator;

    protected override void SubscribeToEvents()
    {
        Subscribe<UserEvent.Registered>();
        Subscribe<UserEvent.Invited>();
        Subscribe<UserEvent.Approved>();
        Subscribe<UserEvent.ConfirmedEmail>();
        Subscribe<UserEvent.PasswordResetRequested>();
        Subscribe<UserEvent.InviteAccepted>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        ApplicationUser user = (evt as UserEvent).User;
        IEmailSender emailSender;
        switch (evt)
        {
            case UserEvent.Registered ev:
                // can be either a self-registration or by invite from another user
                var type = await UserManager.IsInRoleAsync(user, Roles.ServerAdmin) ? "admin" : "user";
                var info = ev switch
                {
                    UserEvent.Invited { InvitedByUser: { } invitedBy } => $"invited by {invitedBy.Email}",
                    UserEvent.Invited => "invited",
                    _ => "registered"
                };
                var requiresApproval = user.RequiresApproval && !user.Approved;
                var requiresEmailConfirmation = user.RequiresEmailConfirmation && !user.EmailConfirmed;

                // log registration info
                var newUserInfo = $"New {type} {user.Email} {info}";
                Logs.PayServer.LogInformation(newUserInfo);

                // send notification if the user does not require email confirmation.
                // inform admins only about qualified users and not annoy them with bot registrations. 
                if (requiresApproval && !requiresEmailConfirmation)
                {
                    await NotifyAdminsAboutUserRequiringApproval(user, ev.ApprovalLink, newUserInfo);
                }

                // set callback result and send email to user
                emailSender = await emailSenderFactory.GetEmailSender();
                if (ev is UserEvent.Invited invited)
                {
                    if (invited.SendInvitationEmail)
                        emailSender.SendInvitation(user.GetMailboxAddress(), invited.InvitationLink);
                }
                else if (requiresEmailConfirmation)
                {
                    emailSender.SendEmailConfirmation(user.GetMailboxAddress(), ev.ConfirmationEmailLink);
                }
                break;

            case UserEvent.PasswordResetRequested pwResetEvent:
                Logs.PayServer.LogInformation("User {Email} requested a password reset", user.Email);
                emailSender = await emailSenderFactory.GetEmailSender();
                emailSender.SendResetPassword(user.GetMailboxAddress(), pwResetEvent.ResetLink);
                break;

            case UserEvent.Approved approvedEvent:
                if (!user.Approved) break;
                emailSender = await emailSenderFactory.GetEmailSender();
                emailSender.SendApprovalConfirmation(user.GetMailboxAddress(), approvedEvent.LoginLink);
                break;

            case UserEvent.ConfirmedEmail confirmedEvent:
                if (!user.EmailConfirmed) break;
                var confirmedUserInfo = $"User {user.Email} confirmed their email address";
                Logs.PayServer.LogInformation(confirmedUserInfo);
                await NotifyAdminsAboutUserRequiringApproval(user, confirmedEvent.ApprovalLink, confirmedUserInfo);
                break;

            case UserEvent.InviteAccepted inviteAcceptedEvent:
                Logs.PayServer.LogInformation("User {Email} accepted the invite", user.Email);
                await NotifyAboutUserAcceptingInvite(user, inviteAcceptedEvent.StoreUsersLink);
                break;
        }
    }

    private async Task NotifyAdminsAboutUserRequiringApproval(ApplicationUser user, string approvalLink, string newUserInfo)
    {
        if (!user.RequiresApproval || user.Approved) return;
        // notification
        await notificationSender.SendNotification(new AdminScope(), new NewUserRequiresApprovalNotification(user));
        // email
        var admins = await UserManager.GetUsersInRoleAsync(Roles.ServerAdmin);
        var emailSender = await emailSenderFactory.GetEmailSender();
        foreach (var admin in admins)
        {
            emailSender.SendNewUserInfo(admin.GetMailboxAddress(), newUserInfo, approvalLink);
        }
    }

    private async Task NotifyAboutUserAcceptingInvite(ApplicationUser user, string storeUsersLink)
    {
        var stores = await storeRepository.GetStoresByUserId(user.Id);
        var notifyRoles = new[] { StoreRoleId.Owner, StoreRoleId.Manager };
        foreach (var store in stores)
        {
            // notification
            await notificationSender.SendNotification(new StoreScope(store.Id, notifyRoles), new InviteAcceptedNotification(user, store));
            // email
            var notifyUsers = await storeRepository.GetStoreUsers(store.Id, notifyRoles);
            var link = string.Format(storeUsersLink, store.Id);
            var emailSender = await emailSenderFactory.GetEmailSender(store.Id);
            foreach (var storeUser in notifyUsers)
            {
                if (storeUser.Id == user.Id) continue; // do not notify the user itself (if they were added as owner or manager)
                var notifyUser = await UserManager.FindByIdOrEmail(storeUser.Id);
                var info = $"User {user.Email} accepted the invite to {store.StoreName}";
                emailSender.SendUserInviteAcceptedInfo(notifyUser.GetMailboxAddress(), info, link);
            }
        }
    }
}
