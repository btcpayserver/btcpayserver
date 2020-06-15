using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Events.Notifications;
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
        public bool Seen { get; set; }
    }

    public static class NotificationViewModelExt
    {
        public static NotificationViewModel ViewModel(this NotificationData data)
        {
            var baseType = typeof(NotificationBase);

            var fullTypeName = baseType.FullName.Replace(nameof(NotificationBase), data.NotificationType, StringComparison.OrdinalIgnoreCase);
            var parsedType = baseType.Assembly.GetType(fullTypeName);
            var instance = Activator.CreateInstance(parsedType) as NotificationBase;

            var casted = JsonConvert.DeserializeObject(ZipUtils.Unzip(data.Blob), parsedType);
            var obj = new NotificationViewModel
            {
                Id = data.Id,
                Created = data.Created,
                Seen = data.Seen
            };

            instance.FillViewModel(obj);

            return obj;
        }
    }
}
