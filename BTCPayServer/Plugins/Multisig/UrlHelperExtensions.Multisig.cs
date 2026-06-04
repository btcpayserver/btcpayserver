#nullable enable
using BTCPayServer.Abstractions;
using BTCPayServer.Plugins.Multisig.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Plugins.Multisig;

public static class UrlHelperExtensions
{
    public static string MultisigSetupSessionLink(this LinkGenerator linkGenerator, string requestId, RequestBaseUrl requestBaseUrl)
        => linkGenerator.GetUriByAction(
            action: nameof(UIMultisigStatusController.Status),
            controller: "UIMultisigStatus",
            values:  new { area = MultisigPlugin.Area, multisigSetupId = requestId },
            requestBaseUrl: requestBaseUrl);

    public static string CreateSignerKeyLink(this LinkGenerator linkGenerator, string requestId, RequestBaseUrl requestBaseUrl)
    => linkGenerator.GetUriByAction(
            action: nameof(UIMultisigSignerKeyController.SubmitMultisigSigner),
            controller: "UIMultisigSignerKey",
            values:  new { area = MultisigPlugin.Area, multisigSetupId = requestId },
            requestBaseUrl: requestBaseUrl);
}
