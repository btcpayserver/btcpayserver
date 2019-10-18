using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace BTCPayServer.Security
{
    public class PolicyRequirement : IAuthorizationRequirement
    {
        public PolicyRequirement(string policy)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            Policy = policy;
        }
        public string Policy { get; }
    }
}
