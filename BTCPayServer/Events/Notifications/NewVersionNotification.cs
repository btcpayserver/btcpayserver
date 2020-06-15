using BTCPayServer.Data;
using BTCPayServer.Models.NotificationViewModels;
using Newtonsoft.Json;

namespace BTCPayServer.Events.Notifications
{
    internal class NewVersionNotification : NotificationBase
    {
        internal override string NotificationType => "NewVersionNotification";

        public string Version { get; set; }

        public override void FillViewModel(NotificationViewModel vm)
        {
            vm.Body = $"New version {Version} released!";
            vm.ActionLink = $"https://github.com/btcpayserver/btcpayserver/releases/tag/v{Version}";
        }
    }
}
