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

namespace BTCPayServer.HostedServices
{
    public class UserEventHostedService : EventHostedServiceBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly EmailSenderFactory _emailSenderFactory;
        private readonly LinkGenerator _generator;

        public UserEventHostedService(EventAggregator eventAggregator, UserManager<ApplicationUser> userManager,
            EmailSenderFactory emailSenderFactory, LinkGenerator generator) : base(eventAggregator)
        {
            _userManager = userManager;
            _emailSenderFactory = emailSenderFactory;
            _generator = generator;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<UserRegisteredEvent>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            switch (evt)
            {
                case UserRegisteredEvent userRegisteredEvent:
                    Logs.PayServer.LogInformation($"A new user just registered {userRegisteredEvent.User.Email} {(userRegisteredEvent.Admin ? "(admin)" : "")}");
                    if (!userRegisteredEvent.User.EmailConfirmed && userRegisteredEvent.User.RequiresEmailConfirmation)
                    {

                        var code = await _userManager.GenerateEmailConfirmationTokenAsync(userRegisteredEvent.User);
                        var callbackUrl = _generator.EmailConfirmationLink(userRegisteredEvent.User.Id, code, userRegisteredEvent.RequestUri.Scheme, new HostString(userRegisteredEvent.RequestUri.Host, userRegisteredEvent.RequestUri.Port), userRegisteredEvent.RequestUri.PathAndQuery);

                        _emailSenderFactory.GetEmailSender()
                            .SendEmailConfirmation(userRegisteredEvent.User.Email, callbackUrl);
                    }
                    break;
            }
        }
    }
}
