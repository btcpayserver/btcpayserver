using System;
using BTCPayServer.Models.NotificationViewModels;

namespace BTCPayServer.Services.Notifications
{
    public interface INotificationHandler
    {
        string NotificationType { get; }
        Type NotificationBlobType { get; }
        void FillViewModel(object notification, NotificationViewModel vm);
    }
    public abstract class NotificationHandler<TNotification> : INotificationHandler
    {
        public abstract string NotificationType { get; }
        Type INotificationHandler.NotificationBlobType => typeof(TNotification);
        void INotificationHandler.FillViewModel(object notification, NotificationViewModel vm)
        {
            FillViewModel((TNotification)notification, vm);
        }
        protected abstract void FillViewModel(TNotification notification, NotificationViewModel vm);
    }
}
