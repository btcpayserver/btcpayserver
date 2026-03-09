#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace BTCPayServer.Security.Greenfield;

public abstract class GreenfieldAuthenticationHandler(
    IOptionsMonitor<GreenfieldAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<MvcNewtonsoftJsonOptions> mvcOptions)
    : AuthenticationHandler<GreenfieldAuthenticationOptions>(options, logger, encoder)
{
    public const string GreenfieldAuthFailureReason = nameof(GreenfieldAuthFailureReason);

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Context.Items.TryGetValue(GreenfieldAuthFailureReason, out var reason);
        var reasonStr = reason as string ?? "Authentication is required for accessing this endpoint";
        await WriteError(new GreenfieldAPIError("unauthenticated", reasonStr), 401);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        if (Context.Items.TryGetValue(GreenfieldAuthorizationHandler.RequestedPermissionKey, out var p) &&
            p is string policy)
        {
            await WriteError(new GreenfieldPermissionAPIError(policy), 403);
        }
        else
        {
            await base.HandleForbiddenAsync(properties);
        }
    }

    private async Task WriteError(object outputObj, int httpCode)
    {
        if (Context.Response.HasStarted)
            return;
        var output = JsonConvert.SerializeObject(outputObj, mvcOptions.Value.SerializerSettings);
        var outputBytes = new UTF8Encoding(false).GetBytes(output);
        Context.Response.ContentType = "application/json";
        Context.Response.ContentLength = outputBytes.Length;
        Context.Response.StatusCode = httpCode;
        await Context.Response.Body.WriteAsync(outputBytes, 0, outputBytes.Length);
    }
}
