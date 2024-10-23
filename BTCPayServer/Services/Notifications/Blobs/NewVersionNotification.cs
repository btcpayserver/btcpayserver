using BTCPayServer.Abstractions.Contracts;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Services.Notifications.Blobs
{
    internal class NewVersionNotification : BaseNotification
    {
        private const string TYPE = "newversion";
        internal class Handler(IStringLocalizer stringLocalizer) : NotificationHandler<NewVersionNotification>
        {
            private IStringLocalizer StringLocalizer { get; } = stringLocalizer;
            public override string NotificationType => TYPE;
            public override (string identifier, string name)[] Meta
            {
                get
                {
                    return [(TYPE, StringLocalizer["New version"])];
                }
            }

            protected override void FillViewModel(NewVersionNotification notification, NotificationViewModel vm)
            {
                vm.Identifier = notification.Identifier;
                vm.Type = notification.NotificationType;
                vm.Body = StringLocalizer["New version {0} released!", notification.Version];
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
