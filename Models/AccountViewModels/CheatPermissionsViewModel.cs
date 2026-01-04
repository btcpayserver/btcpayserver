using Microsoft.AspNetCore.Authorization;

namespace BTCPayServer.Models.AccountViewModels
{
    public class CheatPermissionsViewModel
    {
        public string StoreId { get; internal set; }
        public (string, AuthorizationResult Result)[] Permissions { get; set; }
    }
}
