using System;
using BTCPayServer.Data;
using BTCPayServer.Models.NotificationViewModels;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Notifications.Blobs
{
    // Make sure to keep all Blob Notification classes in same namespace
    // because of dependent initialization and parsing to view models logic
    // IndexViewModel.cs#32
    public abstract class BaseNotification
    {
        public abstract void FillViewModel(NotificationViewModel data);
    }
}
