using OpenIddict.EntityFrameworkCore.Models;

namespace BTCPayServer.Data
{
    public class BTCPayOpenIdAuthorization : OpenIddictAuthorization<string, BTCPayOpenIdClient, BTCPayOpenIdToken> { }
}
