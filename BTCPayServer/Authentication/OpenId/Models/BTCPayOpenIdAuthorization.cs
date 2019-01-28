using OpenIddict.EntityFrameworkCore.Models;

namespace BTCPayServer.Authentication.OpenId.Models
{
    public class BTCPayOpenIdAuthorization : OpenIddictAuthorization<string, BTCPayOpenIdClient, BTCPayOpenIdToken> { }
}