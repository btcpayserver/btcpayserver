using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

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

    internal class Handler(LinkGenerator linkGenerator, BTCPayServerOptions options, IStringLocalizer stringLocalizer)
        : NotificationHandler<InviteAcceptedNotification>
    {
        private IStringLocalizer StringLocalizer { get; } = stringLocalizer;
        public override string NotificationType => TYPE;
        public override (string identifier, string name)[] Meta
        {
            get
            {
                return [(TYPE, StringLocalizer["User accepted invitation"])];
            }
        }

        protected override void FillViewModel(InviteAcceptedNotification notification, NotificationViewModel vm)
        {
            vm.Identifier = notification.Identifier;
            vm.Type = notification.NotificationType;
            vm.StoreId = notification.StoreId;
            vm.Body = StringLocalizer["User {0} accepted the invite to {1}.", notification.UserEmail, notification.StoreName];
            vm.ActionLink = linkGenerator.GetPathByAction(nameof(UIStoresController.StoreUsers),
                "UIStores",
                new { storeId = notification.StoreId }, options.RootPath);
        }
    }
}
