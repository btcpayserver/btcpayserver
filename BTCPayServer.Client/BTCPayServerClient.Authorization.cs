using System;
using System.Collections.Generic;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public static Uri GenerateAuthorizeUri(Uri btcpayHost, string[] permissions, bool strict = true,
        bool selectiveStores = false, (string ApplicationIdentifier, Uri Redirect) applicationDetails = default)
    {
        var result = new UriBuilder(btcpayHost) { Path = "api-keys/authorize" };
        AppendPayloadToQuery(result,
            new Dictionary<string, object>
            {
                {"strict", strict}, {"selectiveStores", selectiveStores}, {"permissions", permissions}
            });

        if (applicationDetails.Redirect != null)
        {
            AppendPayloadToQuery(result, new KeyValuePair<string, object>("redirect", applicationDetails.Redirect));
            if (!string.IsNullOrEmpty(applicationDetails.ApplicationIdentifier))
            {
                AppendPayloadToQuery(result, new KeyValuePair<string, object>("applicationIdentifier", applicationDetails.ApplicationIdentifier));
            }
        }

        return result.Uri;
    }
}
