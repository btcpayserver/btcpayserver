#nullable enable
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Emails.HostedServices;

public class StoreInvitationEventHostedService(
    EventAggregator eventAggregator,
    IServiceScopeFactory serviceScopeFactory,
    NotificationSender notificationSender,
    Logs logs)
    : EventHostedServiceBase(eventAggregator, logs)
{
    protected override void SubscribeToEvents()
    {
        SubscribeAny<StoreUserInvitationEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        switch (evt)
        {
            case StoreUserInvitationEvent.Created created:
                await OnCreated(created);
                break;
            case StoreUserInvitationEvent.Accepted accepted:
                await OnAccepted(accepted);
                break;
        }
    }

    private async Task OnCreated(StoreUserInvitationEvent.Created evt)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var storeRepo = scope.ServiceProvider.GetRequiredService<StoreRepository>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var store = await storeRepo.FindStore(evt.Invitation.StoreId);
        var user = await userManager.FindByIdAsync(evt.Invitation.UserId);
        if (store is null || user is null)
            return;

        await notificationSender.SendNotification(new UserScope(evt.Invitation.UserId),
            new StoreInvitationNotification(store.Id, store.StoreName, evt.InvitationsLink));

        if (string.IsNullOrWhiteSpace(evt.InvitationsLink))
            return;

        var role = StoreRoleId.Parse(evt.Invitation.RoleId).Role;
        var model = new JObject
        {
            ["User"] = new JObject
            {
                ["Name"] = user.UserName,
                ["Email"] = user.Email,
                ["MailboxAddress"] = user.GetMailboxAddress().ToString()
            },
            ["StoreInvitation"] = new JObject
            {
                ["StoreName"] = store.StoreName,
                ["Role"] = role,
                ["Link"] = evt.InvitationsLink
            }
        };
        EventAggregator.Publish(new TriggerEvent(evt.Invitation.StoreId, StoreMailTriggers.StoreInvitePending, model, null));
    }

    private async Task OnAccepted(StoreUserInvitationEvent.Accepted evt)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(evt.Invitation.UserId);
        if (user is null)
            return;

        var model = new JObject
        {
            ["JoinedUser"] = new JObject
            {
                ["Name"] = user.UserName,
                ["Email"] = user.Email,
                ["MailboxAddress"] = user.GetMailboxAddress().ToString(),
                ["Role"] = StoreRoleId.Parse(evt.Invitation.RoleId).Role
            }
        };
        EventAggregator.Publish(new TriggerEvent(evt.Invitation.StoreId, StoreMailTriggers.UserJoined, model, null));
    }
}
