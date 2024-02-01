using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Security;

public static class IdentityBuilderExtension
{
    public static IdentityBuilder AddInvitationTokenProvider(this IdentityBuilder builder)
    {
        var userType = builder.UserType;
        var provider = typeof(InvitationTokenProvider<>).MakeGenericType(userType);
        return builder.AddTokenProvider(InvitationTokenProviderOptions.ProviderName, provider);
    }
}
