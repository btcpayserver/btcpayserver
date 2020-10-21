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
            if (storeId == null)
                throw new ArgumentNullException(nameof(storeId));
            StoreId = storeId;
        }
        public string StoreId { get; }
    }
    public class UserScope : NotificationScope
    {
        public UserScope(string userId)
        {
            if (userId == null)
                throw new ArgumentNullException(nameof(userId));
            UserId = userId;
        }
        public string UserId { get; }
    }

    public interface NotificationScope
    {
    }
}
