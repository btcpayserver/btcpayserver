using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Events.Notifications;
using ExchangeSharp;
using Newtonsoft.Json;

namespace BTCPayServer.Models.NotificationViewModels
{
    public class IndexViewModel
    {
        public List<NotificationViewModel> Items { get; set; }
    }

    public class NotificationViewModel
    {
        public string Id { get; set; }
        public DateTimeOffset Created { get; set; }
        public string Body { get; set; }
        public string ActionLink { get; set; }
    }

    public static class NotificationViewModelExt
    {
        public static NotificationViewModel ViewModel(this NotificationData data)
        {
            if (data.NotificationType == nameof(NewVersionNotification))
            {
                var casted = JsonConvert.DeserializeObject<NewVersionNotification>(data.Blob.ToStringFromUTF8());
                var obj = new NotificationViewModel
                {
                    Id = data.Id,
                    Created = data.Created,
                    Body = $"New version {casted.Version} released!",
                    ActionLink = "https://github.com/btcpayserver/btcpayserver/releases/tag/v" + casted.Version
                };

                return obj;
            }

            return null;
        }
    }
}
