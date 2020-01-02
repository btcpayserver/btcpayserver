using OpenIddict.EntityFrameworkCore.Models;

namespace BTCPayServer.Data
{
    public class BTCPayOpenIdClient: OpenIddictApplication<string, BTCPayOpenIdAuthorization, BTCPayOpenIdToken>
    {
        public string ApplicationUserId { get; set; }
        public ApplicationUser ApplicationUser { get; set; }
    }
}
