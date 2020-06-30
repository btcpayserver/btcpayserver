using System;
using System.Collections.Generic;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {

        public static Uri GenerateAuthorizeUri(Uri btcpayHost, string[] permissions, bool strict = true,
            bool selectiveStores = false)
        {
            var result = new UriBuilder(btcpayHost);
            result.Path = "api-keys/authorize";

            AppendPayloadToQuery(result,
                new Dictionary<string, object>()
                {
                    {"strict", strict}, {"selectiveStores", selectiveStores}, {"permissions", permissions}
                });

            return result.Uri;
        }
    }
}
