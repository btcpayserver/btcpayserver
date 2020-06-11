using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.NotificationViewModels;
using ExchangeSharp;
using Newtonsoft.Json;

namespace BTCPayServer.Events.Notifications
{
    public class NewVersionNotification : NotificationEventBase
    {
        public string Version { get; set; }

        public override NotificationViewModel ToViewModel(NotificationData data)
        {
            var casted = JsonConvert.DeserializeObject<NewVersionNotification>(data.Blob.ToStringFromUTF8());
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
