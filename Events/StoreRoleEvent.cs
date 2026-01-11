#nullable enable

namespace BTCPayServer.Events;

public abstract class StoreRoleEvent(string storeId, string roleId)
{
    public string StoreId { get; } = storeId;
    public string RoleId { get; } = roleId;

    public class Added(string storeId, string roleId) : StoreRoleEvent(storeId, roleId)
    {
        protected override string ToString()
        {
            return $"{base.ToString()} has been added";
        }
    }
    public class Removed(string storeId, string roleId) : StoreRoleEvent(storeId, roleId)
    {
        protected override string ToString()
        {
            return $"{base.ToString()} has been removed";
        }
    }
    public class Updated(string storeId, string roleId) : StoreRoleEvent(storeId, roleId)
    {
        protected override string ToString()
        {
            return $"{base.ToString()} has been updated";
        }
    }

    protected new virtual string ToString()
    {
        return $"StoreRoleEvent: Store {StoreId}, Role {RoleId}";
    }
}
