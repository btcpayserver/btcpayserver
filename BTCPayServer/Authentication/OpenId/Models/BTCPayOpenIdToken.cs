using OpenIddict.EntityFrameworkCore.Models;

namespace BTCPayServer.Authentication.OpenId.Models
{
    public class BTCPayOpenIdToken : OpenIddictToken<string, BTCPayOpenIdClient, BTCPayOpenIdAuthorization> { }
}