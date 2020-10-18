using System;
using BTCPayServer.Contracts;
using BTCPayServer.Models.NotificationViewModels;

namespace BTCPayServer.Services.Notifications
{

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
