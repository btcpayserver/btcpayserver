using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Models.NotificationViewModels;

namespace BTCPayServer.Services.Notifications.Blobs
{
    internal class NewVersionNotification : BaseNotification
    {
        private const string TYPE = "newversion";
        internal class Handler : NotificationHandler<NewVersionNotification>
        {
            public override string NotificationType => TYPE;
            public override (string identifier, string name)[] Meta
            {
                get
                {
                    return new (string identifier, string name)[] { (TYPE, "New version") };
                }
            }

            protected override void FillViewModel(NewVersionNotification notification, NotificationViewModel vm)
            {
                vm.Identifier = notification.Identifier;
                vm.Type = notification.NotificationType;
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
        public override string Identifier => TYPE;
        public override string NotificationType => TYPE;
    }
}
