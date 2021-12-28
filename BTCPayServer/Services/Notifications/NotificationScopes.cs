using System;

namespace BTCPayServer.Services.Notifications
{
    public class AdminScope : NotificationScope
    {
        public AdminScope()
        {
        }
    }
    public class StoreScope : NotificationScope
    {
        public StoreScope(string storeId)
        {
            ArgumentNullException.ThrowIfNull(storeId);
            StoreId = storeId;
        }
        public string StoreId { get; }
    }
    public class UserScope : NotificationScope
    {
        public UserScope(string userId)
        {
            ArgumentNullException.ThrowIfNull(userId);
            UserId = userId;
        }
        public string UserId { get; }
    }

    public interface NotificationScope
    {
    }
}
