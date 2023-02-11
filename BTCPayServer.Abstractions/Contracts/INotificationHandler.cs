using System;

namespace BTCPayServer.Abstractions.Contracts
{
    public abstract class BaseNotification
    {
        public abstract string Identifier { get; }
        public abstract string NotificationType { get; }
    }

    public interface INotificationHandler
    {
        string NotificationType { get; }
        Type NotificationBlobType { get; }
        public (string identifier, string name)[] Meta { get; }
        void FillViewModel(object notification, NotificationViewModel vm);
    }

    public class NotificationViewModel
    {
        public string Id { get; set; }
        public string Identifier { get; set; }
        public string Type { get; set; }
        public DateTimeOffset Created { get; set; }
        public string Body { get; set; }
        public string ActionLink { get; set; }
        public bool Seen { get; set; }
    }
}
