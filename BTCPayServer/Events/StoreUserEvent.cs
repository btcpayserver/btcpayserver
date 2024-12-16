#nullable enable

namespace BTCPayServer.Events;

public abstract class StoreUserEvent(string storeId, string userId)
{
    public class Added(string storeId, string userId, string roleId) : StoreUserEvent(storeId, userId)
    {
        public string RoleId { get; } = roleId;
        protected override string ToString()
        {
            return $"{base.ToString()} has been added";
        }
    }
    public class Removed(string storeId, string userId) : StoreUserEvent(storeId, userId)
    {
        protected override string ToString()
        {
            return $"{base.ToString()} has been removed";
        }
    }
    public class Updated(string storeId, string userId, string roleId) : StoreUserEvent(storeId, userId)
    {
        public string RoleId { get; } = roleId;
        protected override string ToString()
        {
            return $"{base.ToString()} has been updated";
        }
    }

    public string StoreId { get; } = storeId;
    public string UserId { get; } = userId;

    protected new virtual string ToString()
    {
        return $"StoreUserEvent: User {UserId}, Store {StoreId}";
    }
}
