using System;
using Microsoft.AspNetCore.Authorization;

namespace BTCPayServer.Security
{
    public class PolicyRequirement : IAuthorizationRequirement
    {
        public PolicyRequirement(string policy)
        {
            ArgumentNullException.ThrowIfNull(policy);
            Policy = policy;
        }
        public string Policy { get; }
    }
}
