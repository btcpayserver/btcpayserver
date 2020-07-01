using BTCPayServer.Models.NotificationViewModels;

namespace BTCPayServer.Services.Notifications.Blobs
{
    internal class NewVersionNotification
    {
        internal class Handler : NotificationHandler<NewVersionNotification>
        {
            public override string NotificationType => "newversion";
            protected override void FillViewModel(NewVersionNotification notification, NotificationViewModel vm)
            {
                vm.Body = $"New version {notification.Version} released!";
                vm.ActionLink = $"https://github.com/btcpayserver/btcpayserver/releases/tag/v{notification.Version}";
            }
        }
        public NewVersionNotification()
        {

        }
        public NewVersionNotification(string version)
        {
            Version = version;
        }
        public string Version { get; set; }
    }
}
