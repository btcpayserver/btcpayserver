#if DEBUG
using BTCPayServer.Models.NotificationViewModels;

namespace BTCPayServer.Services.Notifications.Blobs
{
    internal class JunkNotification
    {
        internal class Handler : NotificationHandler<JunkNotification>
        {
            public override string NotificationType => "junk";

            protected override void FillViewModel(JunkNotification notification, NotificationViewModel vm)
            {
                vm.Body = $"All your junk r belong to us!";
            }
        }
    }
}
#endif
