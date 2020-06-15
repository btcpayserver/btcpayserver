using BTCPayServer.Services.Notifications.Blobs;

namespace BTCPayServer.Events
{
    internal class NotificationEvent
    {
        internal string[] ApplicationUserIds { get; set; }
        internal NotificationBase Notification { get; set; }
    }
}
