using System;
using System.Linq;
using System.Text.RegularExpressions;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace BTCPayServer.Security.Greenfield
{
    public static class APIKeyExtensions
    {
        public static bool GetAPIKey(this HttpContext httpContext, out StringValues apiKey)
        {
            apiKey = default;
            if (httpContext.Request.Headers.TryGetValue("Authorization", out var value))
            {
                var match = Regex.Match(value.ToString(), @"^(token|bearer)\s+(\S+)", RegexOptions.IgnoreCase);
                if (!match.Success) return false;
                apiKey = match.Groups[2].Value;
                return true;
            }
            return false;
        }

        public static AuthenticationBuilder AddAPIKeyAuthentication(this AuthenticationBuilder builder)
        {
            builder.AddScheme<GreenfieldAuthenticationOptions, APIKeysAuthenticationHandler>(AuthenticationSchemes.GreenfieldAPIKeys,
                _ => { });
            builder.AddScheme<GreenfieldAuthenticationOptions, BasicAuthenticationHandler>(AuthenticationSchemes.GreenfieldBasic,
                _ => { });
            return builder;
        }

        public static string[] GetPermissions(this AuthorizationHandlerContext context)
        {
            return context.User.Claims.Where(c =>
                    c.Type.Equals(GreenfieldConstants.ClaimTypes.Permission, StringComparison.InvariantCultureIgnoreCase))
                .Select(claim => claim.Value).ToArray();
        }

        public static bool HasPermission(this AuthorizationHandlerContext context, Permission permission, PermissionService permissionService, bool anyScope = false)
        {
            foreach (var claim in context.User.Claims.Where(c =>
                c.Type.Equals(GreenfieldConstants.ClaimTypes.Permission, StringComparison.InvariantCultureIgnoreCase)))
            {
                if (Permission.TryParse(claim.Value, out var claimPermission))
                {
                    if (permissionService.Contains(claimPermission, permission, anyScope))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
