using System;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Security;

// https://andrewlock.net/implementing-custom-token-providers-for-passwordless-authentication-in-asp-net-core-identity/
public class InvitationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public const string ProviderName = "InvitationTokenProvider";

    public InvitationTokenProviderOptions()
    {
        Name = ProviderName;
        TokenLifespan = TimeSpan.FromDays(7);
    }
}

public class InvitationTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<InvitationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;
