using BTCPayServer.Models;
using OpenIddict.EntityFrameworkCore.Models;

namespace BTCPayServer.Authentication.OpenId.Models
{
    public class BTCPayOpenIdClient: OpenIddictApplication<string, BTCPayOpenIdAuthorization, BTCPayOpenIdToken>
    {
        public string ApplicationUserId { get; set; }
        public ApplicationUser ApplicationUser { get; set; }
    }
}
