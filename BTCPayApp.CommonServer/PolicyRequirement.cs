using System;
using Microsoft.AspNetCore.Authorization;

namespace BTCPayApp.CommonServer
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
