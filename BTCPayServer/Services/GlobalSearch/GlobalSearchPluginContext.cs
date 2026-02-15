using System.Collections.Generic;
using System.Security.Claims;
using BTCPayServer.Data;

namespace BTCPayServer.Services.GlobalSearch
{
    public class GlobalSearchPluginContext
    {
        public GlobalSearchPluginContext(
            string requestedStoreId,
            int take,
            ClaimsPrincipal user,
            string userId,
            bool isServerAdmin,
            StoreData store,
            GlobalSearchQuery query,
            IList<GlobalSearchResult> results)
        {
            RequestedStoreId = requestedStoreId;
            Take = take;
            User = user;
            UserId = userId;
            IsServerAdmin = isServerAdmin;
            Store = store;
            Query = query;
            Results = results;
        }

        public string RequestedStoreId { get; }
        public int Take { get; }
        public ClaimsPrincipal User { get; }
        public string UserId { get; }
        public bool IsServerAdmin { get; }
        public StoreData Store { get; }
        public GlobalSearchQuery Query { get; }
        public IList<GlobalSearchResult> Results { get; }
    }
}
