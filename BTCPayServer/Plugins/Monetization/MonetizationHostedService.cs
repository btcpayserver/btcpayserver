using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Plugins.Monetization;

public class MonetizationHostedService(
    EventAggregator eventAggregator,
    SettingsRepository settingsRepository,
    UserManager<ApplicationUser> userManager,
    UserService userService,
    ISettingsAccessor<MonetizationSettings> monetizationSettingsAccessor,
    CallbackGenerator callbackGenerator,
    Logs logger) : EventHostedServiceBase(eventAggregator, logger)
{
    protected override void SubscribeToEvents()
    {
        this.Subscribe<SubscriptionEvent.NewSubscriber>();
        this.Subscribe<SubscriptionEvent.SubscriberActivated>();
        this.Subscribe<SubscriptionEvent.SubscriberDisabled>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is SubscriptionEvent.SubscriberEvent se && !IsMonetization(se))
            return;
        if (evt is SubscriptionEvent.NewSubscriber newSub)
        {
            var policies = await settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            var email = newSub.Subscriber.Customer.Email.Get();
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                RequiresEmailConfirmation = policies.RequiresConfirmedEmail,
                RequiresApproval = policies.RequiresUserApproval,
                Created = DateTimeOffset.UtcNow,
                Approved = false
            };
            var created = await userManager.CreateAsync(user);
            if (created.Succeeded)
            {
                var invited = await UserEvent.Invited.Create(user!, user, callbackGenerator, newSub.Checkout.BaseUrl, true);
                EventAggregator.Publish(invited);
            }
        }

        if (evt is (SubscriptionEvent.SubscriberActivated or SubscriptionEvent.SubscriberDisabled)
            and SubscriptionEvent.SubscriberEvent { Subscriber: {} sub })
        {
            var email = sub.Customer.Email.Get();
            var user = await userManager.FindByEmailAsync(email ?? "");
            var activated = evt is SubscriptionEvent.SubscriberActivated;
            if (user is not null)
            {
                if (activated)
                {
                    await userService.SetDisabled(user.Id, false);
                }
                else
                {
                    await userService.SetDisabled(user.Id, true);
                }
            }
        }
    }

    private bool IsMonetization(SubscriptionEvent.SubscriberEvent se)
        => monetizationSettingsAccessor.Settings.OfferingId == se.Subscriber.OfferingId;
}
