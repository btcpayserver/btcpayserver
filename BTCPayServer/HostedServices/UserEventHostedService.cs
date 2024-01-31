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
using MimeKit;

namespace BTCPayServer.HostedServices
{
    public class UserEventHostedService : EventHostedServiceBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly EmailSenderFactory _emailSenderFactory;
        private readonly NotificationSender _notificationSender;
        private readonly LinkGenerator _generator;

        public UserEventHostedService(
            EventAggregator eventAggregator,
            UserManager<ApplicationUser> userManager,
            EmailSenderFactory emailSenderFactory,
            NotificationSender notificationSender,
            LinkGenerator generator,
            Logs logs) : base(eventAggregator, logs)
        {
            _userManager = userManager;
            _emailSenderFactory = emailSenderFactory;
            _notificationSender = notificationSender;
            _generator = generator;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<UserRegisteredEvent>();
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
            MailboxAddress address;
            IEmailSender emailSender;
            UserPasswordResetRequestedEvent userPasswordResetRequestedEvent;
            switch (evt)
            {
                case UserRegisteredEvent userRegisteredEvent:
                    user = userRegisteredEvent.User;
                    Logs.PayServer.LogInformation(
                        $"A new user just registered {user.Email} {(userRegisteredEvent.Admin ? "(admin)" : "")}");
                    if (user.RequiresApproval && !user.Approved)
                    {
                        await _notificationSender.SendNotification(new AdminScope(), new NewUserRequiresApprovalNotification(user));
                    }
                    if (!user.EmailConfirmed && user.RequiresEmailConfirmation)
                    {
                        uri = userRegisteredEvent.RequestUri;
                        host = new HostString(uri.Host, uri.Port);
                        code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                        callbackUrl = _generator.EmailConfirmationLink(user.Id, code, uri.Scheme, host, uri.PathAndQuery);
                        userRegisteredEvent.CallbackUrlGenerated?.SetResult(new Uri(callbackUrl));
                        address = user.GetMailboxAddress();
                        emailSender = await _emailSenderFactory.GetEmailSender();
                        emailSender.SendEmailConfirmation(address, callbackUrl);
                    }
                    else if (!await _userManager.HasPasswordAsync(userRegisteredEvent.User))
                    {
                        userPasswordResetRequestedEvent = new UserPasswordResetRequestedEvent
                        {
                            CallbackUrlGenerated = userRegisteredEvent.CallbackUrlGenerated,
                            User = user,
                            RequestUri = userRegisteredEvent.RequestUri
                        };
                        goto passwordSetter;
                    }
                    else
                    {
                        userRegisteredEvent.CallbackUrlGenerated?.SetResult(null);
                    }
                    break;

                case UserApprovedEvent userApprovedEvent:
                    if (userApprovedEvent.Approved)
                    {
                        uri = userApprovedEvent.RequestUri;
                        host = new HostString(uri.Host, uri.Port);
                        address = userApprovedEvent.User.GetMailboxAddress();
                        callbackUrl = _generator.LoginLink(uri.Scheme, host, uri.PathAndQuery);
                        emailSender = await _emailSenderFactory.GetEmailSender();
                        emailSender.SendApprovalConfirmation(address, callbackUrl);
                    }
                    break;

                case UserPasswordResetRequestedEvent userPasswordResetRequestedEvent2:
                    userPasswordResetRequestedEvent = userPasswordResetRequestedEvent2;
passwordSetter:
                    uri = userPasswordResetRequestedEvent.RequestUri;
                    host = new HostString(uri.Host, uri.Port);
                    user = userPasswordResetRequestedEvent.User;
                    code = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var newPassword = await _userManager.HasPasswordAsync(user);
                    callbackUrl = _generator.ResetPasswordCallbackLink(user.Id, code, uri.Scheme, host, uri.PathAndQuery);
                    userPasswordResetRequestedEvent.CallbackUrlGenerated?.SetResult(new Uri(callbackUrl));
                    address = user.GetMailboxAddress();
                    emailSender = await _emailSenderFactory.GetEmailSender();
                    emailSender.SendSetPasswordConfirmation(address, callbackUrl, newPassword);
                    break;
            }
        }
    }
}
