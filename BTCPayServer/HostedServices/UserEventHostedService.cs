using System;
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices;

public class UserEventHostedService(
    EventAggregator eventAggregator,
    UserManager<ApplicationUser> userManager,
    EmailSenderFactory emailSenderFactory,
    NotificationSender notificationSender,
    StoreRepository storeRepository,
    LinkGenerator generator,
    Logs logs)
    : EventHostedServiceBase(eventAggregator, logs)
{
    protected override void SubscribeToEvents()
    {
        Subscribe<UserRegisteredEvent>();
        Subscribe<UserApprovedEvent>();
        Subscribe<UserConfirmedEmailEvent>();
        Subscribe<UserPasswordResetRequestedEvent>();
        Subscribe<UserInviteAcceptedEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        string code;
        string callbackUrl;
        Uri uri;
        HostString host;
        ApplicationUser user;
        IEmailSender emailSender;
        switch (evt)
        {
            case UserRegisteredEvent ev:
                user = ev.User;
                uri = ev.RequestUri;
                host = new HostString(uri.Host, uri.Port);

                // can be either a self-registration or by invite from another user
                var isInvite = ev.Kind == UserRegisteredEventKind.Invite;
                var type = ev.Admin ? "admin" : "user";
                var info = isInvite ? ev.InvitedByUser != null ? $"invited by {ev.InvitedByUser.Email}" : "invited" : "registered";
                var requiresApproval = user.RequiresApproval && !user.Approved;
                var requiresEmailConfirmation = user.RequiresEmailConfirmation && !user.EmailConfirmed;

                // log registration info
                var newUserInfo = $"New {type} {user.Email} {info}";
                Logs.PayServer.LogInformation(newUserInfo);

                // send notification if the user does not require email confirmation.
                // inform admins only about qualified users and not annoy them with bot registrations. 
                if (requiresApproval && !requiresEmailConfirmation)
                {
                    await NotifyAdminsAboutUserRequiringApproval(user, uri, newUserInfo);
                }

                // set callback result and send email to user
                emailSender = await emailSenderFactory.GetEmailSender();
                if (isInvite)
                {
                    code = await userManager.GenerateInvitationTokenAsync<ApplicationUser>(user.Id);
                    callbackUrl = generator.InvitationLink(user.Id, code, uri.Scheme, host, uri.PathAndQuery);
                    ev.CallbackUrlGenerated?.SetResult(new Uri(callbackUrl));

                    if (ev.SendInvitationEmail)
                        emailSender.SendInvitation(user.GetMailboxAddress(), callbackUrl);
                }
                else if (requiresEmailConfirmation)
                {
                    code = await userManager.GenerateEmailConfirmationTokenAsync(user);
                    callbackUrl = generator.EmailConfirmationLink(user.Id, code, uri.Scheme, host, uri.PathAndQuery);
                    ev.CallbackUrlGenerated?.SetResult(new Uri(callbackUrl));

                    emailSender.SendEmailConfirmation(user.GetMailboxAddress(), callbackUrl);
                }
                else
                {
                    ev.CallbackUrlGenerated?.SetResult(null);
                }
                break;

            case UserPasswordResetRequestedEvent pwResetEvent:
                user = pwResetEvent.User;
                uri = pwResetEvent.RequestUri;
                host = new HostString(uri.Host, uri.Port);
                code = await userManager.GeneratePasswordResetTokenAsync(user);
                callbackUrl = generator.ResetPasswordLink(user.Id, code, uri.Scheme, host, uri.PathAndQuery);
                pwResetEvent.CallbackUrlGenerated?.SetResult(new Uri(callbackUrl));
                Logs.PayServer.LogInformation("User {Email} requested a password reset", user.Email);
                emailSender = await emailSenderFactory.GetEmailSender();
                emailSender.SendResetPassword(user.GetMailboxAddress(), callbackUrl);
                break;

            case UserApprovedEvent approvedEvent:
                user = approvedEvent.User;
                if (!user.Approved) break;
                uri = approvedEvent.RequestUri;
                host = new HostString(uri.Host, uri.Port);
                callbackUrl = generator.LoginLink(uri.Scheme, host, uri.PathAndQuery);
                emailSender = await emailSenderFactory.GetEmailSender();
                emailSender.SendApprovalConfirmation(user.GetMailboxAddress(), callbackUrl);
                break;

            case UserConfirmedEmailEvent confirmedEvent:
                user = confirmedEvent.User;
                if (!user.EmailConfirmed) break;
                uri = confirmedEvent.RequestUri;
                var confirmedUserInfo = $"User {user.Email} confirmed their email address";
                Logs.PayServer.LogInformation(confirmedUserInfo);
                if (!user.RequiresApproval || user.Approved) return;
                await NotifyAdminsAboutUserRequiringApproval(user, uri, confirmedUserInfo);
                break;

            case UserInviteAcceptedEvent inviteAcceptedEvent:
                user = inviteAcceptedEvent.User;
                uri = inviteAcceptedEvent.RequestUri;
                Logs.PayServer.LogInformation("User {Email} accepted the invite", user.Email);
                await NotifyAboutUserAcceptingInvite(user, uri);
                break;
        }
    }

    private async Task NotifyAdminsAboutUserRequiringApproval(ApplicationUser user, Uri uri, string newUserInfo)
    {
        if (!user.RequiresApproval || user.Approved) return;
        // notification
        await notificationSender.SendNotification(new AdminScope(), new NewUserRequiresApprovalNotification(user));
        // email
        var admins = await userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
        var host = new HostString(uri.Host, uri.Port);
        var approvalLink = generator.UserDetailsLink(user.Id, uri.Scheme, host, uri.PathAndQuery);
        var emailSender = await emailSenderFactory.GetEmailSender();
        foreach (var admin in admins)
        {
            emailSender.SendNewUserInfo(admin.GetMailboxAddress(), newUserInfo, approvalLink);
        }
    }

    private async Task NotifyAboutUserAcceptingInvite(ApplicationUser user, Uri uri)
    {
        var stores = await storeRepository.GetStoresByUserId(user.Id);
        var notifyRoles = new[] { StoreRoleId.Owner, StoreRoleId.Manager };
        foreach (var store in stores)
        {
            // notification
            await notificationSender.SendNotification(new StoreScope(store.Id, notifyRoles), new InviteAcceptedNotification(user, store));
            // email
            var notifyUsers = await storeRepository.GetStoreUsers(store.Id, notifyRoles);
            var host = new HostString(uri.Host, uri.Port);
            var storeUsersLink = generator.StoreUsersLink(store.Id, uri.Scheme, host, uri.PathAndQuery);
            var emailSender = await emailSenderFactory.GetEmailSender(store.Id);
            foreach (var storeUser in notifyUsers)
            {
                if (storeUser.Id == user.Id) continue; // do not notify the user itself (if they were added as owner or manager)
                var notifyUser = await userManager.FindByIdOrEmail(storeUser.Id);
                var info = $"User {user.Email} accepted the invite to {store.StoreName}";
                emailSender.SendUserInviteAcceptedInfo(notifyUser.GetMailboxAddress(), info, storeUsersLink);
            }
        }
    }
}
