using BTCPayServer.Data;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Security;

public static class IdentityBuilderExtension
{
    public static IdentityBuilder AddInvitationTokenProvider(this IdentityBuilder builder)
    {
        var provider = typeof(InvitationTokenProvider<>).MakeGenericType(typeof(ApplicationUser));
        return builder.AddTokenProvider(InvitationTokenProviderOptions.ProviderName, provider);
    }
}
