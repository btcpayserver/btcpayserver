using System;
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
        var selector = new MultisigXPubSelect(ui, ScriptType);
        var selection = await selector.GetSelection();
        ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Fetching public keys..."]);

        var keyPath = selection.UseCustomPath
            ? selection.CustomParsedKeyPath
            : BuildDefaultPath(network.CoinType, selection.ScriptType, selection.AccountNumber);

        var xpub = await device.GetXPubAsync(keyPath, cancellationToken);

        ui.ShowFeedback(FeedbackType.Success, ui.StringLocalizer["Public keys successfully fetched."]);
        await ui.JSRuntime.InvokeVoidAsync("vault.setMultisigInviteXPub", cancellationToken, new JsonObject
        {
            ["accountKey"] = xpub.ToString(),
            ["masterFingerprint"] = fingerprint.ToString().ToLowerInvariant(),
            ["accountKeyPath"] = $"m/{keyPath}"
        });
    }

    private static KeyPath BuildDefaultPath(KeyPath coinType, string scriptType, int accountNumber)
    {
        accountNumber = Math.Max(0, accountNumber);
        var normalized = scriptType?.ToLowerInvariant();
        return normalized switch
        {
            "p2sh-p2wsh" => new KeyPath("48'").Derive(coinType).Derive(accountNumber, true).Derive(1, true),
            "p2sh" => new KeyPath("45'").Derive(coinType).Derive(accountNumber, true),
            _ => new KeyPath("48'").Derive(coinType).Derive(accountNumber, true).Derive(2, true)
        };
    }
}
