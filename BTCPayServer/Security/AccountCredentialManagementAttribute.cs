#nullable enable
using System;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Security;

/// <summary>
/// Restricts account-level API key management to users permitted by the server credential-management policy.
/// Unlike <see cref="BTCPayServer.Client.Policies.CanManageStoreCredentials"/>, store roles are not consulted.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class AccountCredentialManagementAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var credentialManagementService = context.HttpContext.RequestServices.GetRequiredService<CredentialManagementService>();
        if (!credentialManagementService.CanManageAccountApiKeys(context.HttpContext.User))
            context.Result = new ForbidResult(AuthenticationSchemes.Cookie);
    }
}
