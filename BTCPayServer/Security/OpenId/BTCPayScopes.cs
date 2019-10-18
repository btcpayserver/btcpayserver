using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;

namespace BTCPayServer.Security.OpenId
{
    public static class BTCPayScopes
    {
        public const string StoreManagement = "store_management";
        public const string ServerManagement = "server_management";
    }
    public static class RestAPIPolicies
    {
        
    }
}
