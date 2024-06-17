using System;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Services.Notifications
{

    public abstract class NotificationHandler<TNotification> : INotificationHandler
    {
        public abstract string NotificationType { get; }
        Type INotificationHandler.NotificationBlobType => typeof(TNotification);
        public abstract (string identifier, string name)[] Meta { get; }

        void INotificationHandler.FillViewModel(object notification, NotificationViewModel vm)
        {
            FillViewModel((TNotification)notification, vm);
        }
        protected abstract void FillViewModel(TNotification notification, NotificationViewModel vm);
    }
}
