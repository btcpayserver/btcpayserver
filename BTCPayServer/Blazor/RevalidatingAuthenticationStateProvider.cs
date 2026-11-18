#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Blazor;

public class RevalidatingAuthenticationStateProvider(
    CircuitUserRevalidator circuitUserRevalidator,
    IOptions<SecurityStampValidatorOptions> securityStampValidatorOptions,
    ILoggerFactory loggerFactory) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    // Revoked circuits drop their authentication within the same window as cookies.
    protected override TimeSpan RevalidationInterval => securityStampValidatorOptions.Value.ValidationInterval;

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
        => await circuitUserRevalidator.Revalidate(authenticationState.User) is not null;
}
