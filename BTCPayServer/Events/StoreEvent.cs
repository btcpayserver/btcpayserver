using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class StoreEvent(StoreData store)
{
    public string StoreId { get; } = store.Id;

    public IEnumerable<StoreUser> StoreUsers { get; } = store.UserStores.Select(userStore => new StoreUser
    {
        UserId = userStore.ApplicationUserId,
        RoleId = userStore.StoreRoleId
    });

    protected new virtual string ToString()
    {
        return $"StoreEvent: Store \"{store.StoreName}\" ({store.Id})";
    }

    public class StoreUser
    {
        public string UserId { get; set; }
        public string RoleId { get; set; }
    }
}
