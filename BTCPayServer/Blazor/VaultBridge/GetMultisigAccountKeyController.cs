using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Blazor.VaultBridge.Elements;
using BTCPayServer.Hwi;
using Microsoft.JSInterop;
using NBitcoin;

namespace BTCPayServer.Blazor.VaultBridge;

public class GetMultisigAccountKeyController : HWIController
{
    public string ScriptType { get; set; }

    protected override async Task Run(VaultBridgeUI ui, HwiClient hwi, HwiDeviceClient device, HDFingerprint fingerprint, BTCPayNetwork network, CancellationToken cancellationToken)
    {
        var selector = new MultisigXPubSelect(ui, ScriptType, network.CoinType);
        var selection = await selector.GetSelection();
        ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Fetching public keys..."]);

        var keyPath = selection.UseCustomPath
            ? selection.CustomParsedKeyPath
            : MultisigKeyPathHelper.BuildDefaultPath(network.CoinType, selection.ScriptType, selection.AccountNumber);

        var xpub = await device.GetXPubAsync(keyPath, cancellationToken);

        ui.ShowFeedback(FeedbackType.Success, ui.StringLocalizer["Public keys successfully fetched."]);
        await ui.JSRuntime.InvokeVoidAsync("vault.setMultisigInviteXPub", cancellationToken, new JsonObject
        {
            ["accountKey"] = xpub.ToString(),
            ["masterFingerprint"] = fingerprint.ToString().ToLowerInvariant(),
            ["accountKeyPath"] = $"m/{keyPath}"
        });
    }

}
