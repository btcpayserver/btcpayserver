using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Blazor.VaultBridge;
using BTCPayServer.Blazor.VaultBridge.Elements;
using BTCPayServer.Hwi;
using BTCPayServer.Plugins.Multisig.VaultBridge.Elements;
using Microsoft.JSInterop;
using NBitcoin;

namespace BTCPayServer.Plugins.Multisig.VaultBridge;

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
        await ui.JSRuntime.InvokeVoidAsync("vault.setMultisigSignerKey", cancellationToken, new JsonObject
        {
            ["accountKey"] = xpub.ToString(),
            ["accountKeyPath"] = new RootedKeyPath(fingerprint, keyPath).ToString()
        });
    }
}
