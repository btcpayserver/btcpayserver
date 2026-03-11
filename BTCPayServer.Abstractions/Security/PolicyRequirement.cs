#nullable enable
using System;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Authorization;

namespace BTCPayServer.Security;

public class PolicyRequirement(string policy, bool requireUnscoped = false) : IAuthorizationRequirement
{
    public bool RequireUnscoped { get; } = requireUnscoped;
    public string Policy { get; } = Permission.IsValidPolicy(policy) ? policy : throw new ArgumentException("Invalid policy (it should be 'btcpay.some.permission.name')", nameof(policy));
}
