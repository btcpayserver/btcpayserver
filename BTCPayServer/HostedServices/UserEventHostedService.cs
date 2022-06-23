using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;
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
        private readonly LinkGenerator _generator;


        public UserEventHostedService(EventAggregator eventAggregator, UserManager<ApplicationUser> userManager,
            EmailSenderFactory emailSenderFactory, LinkGenerator generator, Logs logs) : base(eventAggregator, logs)
        {
            _userManager = userManager;
            _emailSenderFactory = emailSenderFactory;
            _generator = generator;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<UserRegisteredEvent>();
            Subscribe<UserPasswordResetRequestedEvent>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            string code;
            string callbackUrl;
            MailboxAddress address;
            UserPasswordResetRequestedEvent userPasswordResetRequestedEvent;
            switch (evt)
            {
                case UserRegisteredEvent userRegisteredEvent:
                    Logs.PayServer.LogInformation(
                        $"A new user just registered {userRegisteredEvent.User.Email} {(userRegisteredEvent.Admin ? "(admin)" : "")}");
                    if (!userRegisteredEvent.User.EmailConfirmed && userRegisteredEvent.User.RequiresEmailConfirmation)
                    {
                        code = await _userManager.GenerateEmailConfirmationTokenAsync(userRegisteredEvent.User);
                        callbackUrl = _generator.EmailConfirmationLink(userRegisteredEvent.User.Id, code,
                            userRegisteredEvent.RequestUri.Scheme,
                            new HostString(userRegisteredEvent.RequestUri.Host, userRegisteredEvent.RequestUri.Port),
                            userRegisteredEvent.RequestUri.PathAndQuery);
                        userRegisteredEvent.CallbackUrlGenerated?.SetResult(new Uri(callbackUrl));
                        address = userRegisteredEvent.User.GetMailboxAddress();
                        (await _emailSenderFactory.GetEmailSender()).SendEmailConfirmation(address, callbackUrl);
                    }
                    else if (!await _userManager.HasPasswordAsync(userRegisteredEvent.User))
                    {
                        userPasswordResetRequestedEvent = new UserPasswordResetRequestedEvent()
                        {
                            CallbackUrlGenerated = userRegisteredEvent.CallbackUrlGenerated,
                            User = userRegisteredEvent.User,
                            RequestUri = userRegisteredEvent.RequestUri
                        };
                        goto passwordSetter;
                    }
                    else
                    {
                        userRegisteredEvent.CallbackUrlGenerated?.SetResult(null);
                    }

                    break;
                case UserPasswordResetRequestedEvent userPasswordResetRequestedEvent2:
                    userPasswordResetRequestedEvent = userPasswordResetRequestedEvent2;
passwordSetter:
                    code = await _userManager.GeneratePasswordResetTokenAsync(userPasswordResetRequestedEvent.User);
                    var newPassword = await _userManager.HasPasswordAsync(userPasswordResetRequestedEvent.User);
                    callbackUrl = _generator.ResetPasswordCallbackLink(userPasswordResetRequestedEvent.User.Id, code,
                        userPasswordResetRequestedEvent.RequestUri.Scheme,
                        new HostString(userPasswordResetRequestedEvent.RequestUri.Host,
                            userPasswordResetRequestedEvent.RequestUri.Port),
                        userPasswordResetRequestedEvent.RequestUri.PathAndQuery);
                    userPasswordResetRequestedEvent.CallbackUrlGenerated?.SetResult(new Uri(callbackUrl));
                    address = userPasswordResetRequestedEvent.User.GetMailboxAddress();
                    (await _emailSenderFactory.GetEmailSender())
                        .SendSetPasswordConfirmation(address, callbackUrl, newPassword);
                    break;
            }
        }
    }
}
