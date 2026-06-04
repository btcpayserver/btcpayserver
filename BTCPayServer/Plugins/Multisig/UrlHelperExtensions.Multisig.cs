#nullable enable
using BTCPayServer.Abstractions;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins.Multisig.Controllers;
using BTCPayServer.Plugins.Wallets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Plugins.Multisig;

public static class UrlHelperExtensions
{
    public static string MultisigSetupSessionLink(this LinkGenerator linkGenerator, string requestId, RequestBaseUrl requestBaseUrl)
        => linkGenerator.GetUriByAction(
            action: nameof(UIMultisigSetupController.SetupMultisigStatus),
            controller: "UIMultisigSetup",
            values:  new { area = MultisigPlugin.Area, multisigSetupId = requestId },
            requestBaseUrl: requestBaseUrl);
}
