using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public bool Seen { get; set; }
    }

    public static class NotificationViewModelExt
    {
        public static NotificationViewModel ViewModel(this NotificationData data)
        {
            var baseType = typeof(NotificationEventBase);

            var typeName = baseType.FullName.Replace(nameof(NotificationEventBase), data.NotificationType, StringComparison.OrdinalIgnoreCase);
            var instance = Activator.CreateInstance(baseType.Assembly.GetType(typeName)) as NotificationEventBase;

            return instance.ToViewModel(data);
        }
    }
}
