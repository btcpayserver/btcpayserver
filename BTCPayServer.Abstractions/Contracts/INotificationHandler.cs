using System;

namespace BTCPayServer.Contracts
{
    public interface INotificationHandler
    {
        string NotificationType { get; }
        Type NotificationBlobType { get; }
        void FillViewModel(object notification, NotificationViewModel vm);
    }
    
    public class NotificationViewModel
    {
        public string Id { get; set; }
        public DateTimeOffset Created { get; set; }
        public string Body { get; set; }
        public string ActionLink { get; set; }
        public bool Seen { get; set; }
    }
}
