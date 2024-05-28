using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Services.Notifications.Blobs;

internal class InviteAcceptedNotification : BaseNotification
{
    private const string TYPE = "inviteaccepted";
    public string UserId { get; set; }
    public string UserEmail { get; set; }
    public string StoreId { get; set; }
    public string StoreName { get; set; }
    public override string Identifier => TYPE;
    public override string NotificationType => TYPE;

    public InviteAcceptedNotification()
    {
    }

    public InviteAcceptedNotification(ApplicationUser user, StoreData store)
    {
        UserId = user.Id;
        UserEmail = user.Email;
        StoreId = store.Id;
        StoreName = store.StoreName;
    }

    internal class Handler(LinkGenerator linkGenerator, BTCPayServerOptions options)
        : NotificationHandler<InviteAcceptedNotification>
    {
        public override string NotificationType => TYPE;
        public override (string identifier, string name)[] Meta
        {
            get
            {
                return [(TYPE, "User accepted invitation")];
            }
        }

        protected override void FillViewModel(InviteAcceptedNotification notification, NotificationViewModel vm)
        {
            vm.Identifier = notification.Identifier;
            vm.Type = notification.NotificationType;
            vm.StoreId = notification.StoreId;
            vm.Body = $"User {notification.UserEmail} accepted the invite to {notification.StoreName}.";
            vm.ActionLink = linkGenerator.GetPathByAction(nameof(UIStoresController.StoreUsers),
                "UIStores",
                new { storeId = notification.StoreId }, options.RootPath);
        }
    }
}
