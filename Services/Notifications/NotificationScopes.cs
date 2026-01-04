using System;
using System.Collections.Generic;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Services.Notifications;

public class AdminScope : INotificationScope;
public class StoreScope : INotificationScope
{
    public StoreScope(string storeId, IEnumerable<StoreRoleId> roles = null)
    {
        ArgumentNullException.ThrowIfNull(storeId);
        StoreId = storeId;
        Roles = roles;
    }
    public string StoreId { get; }
    public IEnumerable<StoreRoleId> Roles { get; set; }
}

public class UserScope : INotificationScope
{
    public UserScope(string userId)
    {
        ArgumentNullException.ThrowIfNull(userId);
        UserId = userId;
    }
    public string UserId { get; }
}

public interface INotificationScope;
