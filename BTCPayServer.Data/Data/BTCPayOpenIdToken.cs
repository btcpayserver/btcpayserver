using OpenIddict.EntityFrameworkCore.Models;

namespace BTCPayServer.Data
{
    public class BTCPayOpenIdToken : OpenIddictToken<string, BTCPayOpenIdClient, BTCPayOpenIdAuthorization> { }
}
