using BTCPayServer.Data;
using BTCPayServer.Models.NotificationViewModels;
using Newtonsoft.Json;

namespace BTCPayServer.Events.Notifications
{
    internal class NewVersionNotification : NotificationBase
    {
        public string Version { get; set; }

        public override NotificationViewModel ToViewModel(NotificationData data)
        {
            var casted = JsonConvert.DeserializeObject<NewVersionNotification>(ZipUtils.Unzip(data.Blob));
            var obj = new NotificationViewModel
            {
                Id = data.Id,
                Created = data.Created,
                Body = $"New version {casted.Version} released!",
                ActionLink = "https://github.com/btcpayserver/btcpayserver/releases/tag/v" + casted.Version,
                Seen = data.Seen
            };

            return obj;
        }
    }
}
