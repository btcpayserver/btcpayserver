using BTCPayServer.Data;
using BTCPayServer.Models.NotificationViewModels;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Notifications.Blobs
{
    internal class NewVersionNotification : BaseNotification
    {
        internal override string NotificationType => "NewVersionNotification";
        public NewVersionNotification()
        {

        }
        public NewVersionNotification(string version)
        {
            Version = version;
        }
        public string Version { get; set; }

        public override void FillViewModel(ref NotificationViewModel vm)
        {
            vm.Body = $"New version {Version} released!";
            vm.ActionLink = $"https://github.com/btcpayserver/btcpayserver/releases/tag/v{Version}";
        }
    }
}
