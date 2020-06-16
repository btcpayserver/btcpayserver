using BTCPayServer.Data;
using BTCPayServer.Models.NotificationViewModels;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Notifications.Blobs
{
    [Notification("newversion")]
    internal class NewVersionNotification : BaseNotification
    {
        public NewVersionNotification()
        {

        }
        public NewVersionNotification(string version)
        {
            Version = version;
        }
        public string Version { get; set; }

        public override void FillViewModel(NotificationViewModel vm)
        {
            vm.Body = $"New version {Version} released!";
            vm.ActionLink = $"https://github.com/btcpayserver/btcpayserver/releases/tag/v{Version}";
        }
    }
}
