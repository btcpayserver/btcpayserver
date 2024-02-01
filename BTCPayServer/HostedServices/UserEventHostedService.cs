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
    LinkGenerator generator,
    Logs logs)
    : EventHostedServiceBase(eventAggregator, logs)
{
    protected override void SubscribeToEvents()
    {
        Subscribe<UserRegisteredEvent>();
        Subscribe<UserInvitedEvent>();
        Subscribe<UserApprovedEvent>();
        Subscribe<UserPasswordResetRequestedEvent>();
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
            case UserInvitedEvent:
            case UserRegisteredEvent:
                var ev = (UserRegisteredEvent)evt;
                user = ev.User;
                uri = ev.RequestUri;
                host = new HostString(uri.Host, uri.Port);

                // can be either a self-registration or by invite from another user
                var isInvite = evt is UserInvitedEvent;
                var type = ev.Admin ? "admin" : "user";
                var info = isInvite ? $"invited by {((UserInvitedEvent)ev).InvitedByUser.Email}" : "registered";
                var requiresApproval = user.RequiresApproval && !user.Approved;
                var requiresEmailConfirmation = user.RequiresEmailConfirmation && !user.EmailConfirmed;

                // log registration info and send notification
                var newUserInfo = $"New {type} {user.Email} {info}";
                Logs.PayServer.LogInformation(newUserInfo);

                emailSender = await emailSenderFactory.GetEmailSender();

                if (requiresApproval)
                {
                    await notificationSender.SendNotification(new AdminScope(), new NewUserRequiresApprovalNotification(user));

                    var admins = await userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
                    var approvalLink = generator.UserDetailsLink(user.Id, uri.Scheme, host, uri.PathAndQuery);
                    foreach (var admin in admins)
                    {
                        emailSender.SendNewUserInfo(admin.GetMailboxAddress(), newUserInfo, approvalLink);
                    }
                }

                // set callback result and send email to user
                if (isInvite)
                {
                    code = await userManager.GenerateInvitationTokenAsync(user);
                    callbackUrl = generator.InvitationLink(user.Id, code, uri.Scheme, host, uri.PathAndQuery);
                    ev.CallbackUrlGenerated?.SetResult(new Uri(callbackUrl));

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

                emailSender = await emailSenderFactory.GetEmailSender();
                emailSender.SendResetPassword(user.GetMailboxAddress(), callbackUrl);
                break;

            case UserApprovedEvent approvedEvent:
                if (!approvedEvent.Approved) break;
                uri = approvedEvent.RequestUri;
                host = new HostString(uri.Host, uri.Port);
                callbackUrl = generator.LoginLink(uri.Scheme, host, uri.PathAndQuery);
                emailSender = await emailSenderFactory.GetEmailSender();
                emailSender.SendApprovalConfirmation(approvedEvent.User.GetMailboxAddress(), callbackUrl);
                break;
        }
    }
}
