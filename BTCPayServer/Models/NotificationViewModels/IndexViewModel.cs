using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Services.Notifications.Blobs;
using Newtonsoft.Json;

namespace BTCPayServer.Models.NotificationViewModels
{
    public class IndexViewModel
    {
        public int Skip { get; set; }
        public int Count { get; set; }
        public int Total { get; set; }
        public List<NotificationViewModel> Items { get; set; }
    }

    public class NotificationViewModel
    {
        public string Id { get; set; }
        public DateTimeOffset Created { get; set; }
        public string Body { get; set; }
        public string ActionLink { get; set; }
        public bool Seen { get; set; }
    }

    public static class NotificationViewModelExt
    {
        public static NotificationViewModel ViewModel(this NotificationData data)
        {
            var baseType = typeof(BaseNotification);

            var fullTypeName = baseType.FullName.Replace(nameof(BaseNotification), data.NotificationType, StringComparison.OrdinalIgnoreCase);
            var parsedType = baseType.Assembly.GetType(fullTypeName);

            var casted = (BaseNotification)JsonConvert.DeserializeObject(ZipUtils.Unzip(data.Blob), parsedType);
            var obj = new NotificationViewModel
            {
                Id = data.Id,
                Created = data.Created,
                Seen = data.Seen
            };

            casted.FillViewModel(ref obj);

            return obj;
        }
    }
}
