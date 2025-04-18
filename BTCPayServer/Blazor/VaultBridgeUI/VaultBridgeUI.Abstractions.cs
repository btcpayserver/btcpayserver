using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Hwi;
using Microsoft.JSInterop;
using NBitcoin;

namespace BTCPayServer.Blazor;


public partial class VaultBridgeUI
{
    public interface IController
    {
        Task Run(VaultBridgeUI ui, CancellationToken cancellationToken);
    }

    public abstract class VaultController : IController
    {
        protected abstract string VaultUri { get; }
        protected abstract Task Run(VaultBridgeUI ui, VaultClient vaultClient, CancellationToken cancellationToken);
        public async Task Run(VaultBridgeUI ui, CancellationToken cancellationToken)
        {
            try
            {
                ui.ShowFeedback(VaultElement.Feedback.StateValue.Loading, ui.StringLocalizer["Checking BTCPay Server Vault is running..."]);
                var client = new VaultClient(ui.JSRuntime, VaultUri);
                var res = await client.AskPermission(cancellationToken);
                var feedback = (status: res.HttpCode, browser: res.Browser) switch
                {
                    (200, _) => new VaultElement.Feedback(ui.StringLocalizer["Access to vault granted by owner."], VaultElement.Feedback.StateValue.Success),
                    (401, _) => new VaultElement.Feedback(ui.StringLocalizer["The user declined access to the vault."],
                        VaultElement.Feedback.StateValue.Failed),
                    (_, "safari") => new VaultElement.Feedback(
                        ui.ViewLocalizer[
                            "Safari doesn't support BTCPay Server Vault. Please use a different browser. (<a class=\"alert-link\" href=\"https://bugs.webkit.org/show_bug.cgi?id=171934\" target=\"_blank\" rel=\"noreferrer noopener\">More information</a>)"],
                        VaultElement.Feedback.StateValue.Failed),
                    _ => new VaultElement.Feedback(
                        ui.ViewLocalizer[
                            "BTCPay Server Vault does not seem to be running, you can download it on <a target=\"_blank\" href=\"https://github.com/btcpayserver/BTCPayServer.Vault/releases/latest\">Github</a>."],
                        VaultElement.Feedback.StateValue.Failed),
                };
                ui.ShowFeedback(feedback);

                if (res.HttpCode != 200)
                {
                    if (res.HttpCode == 0 && res.Browser == "brave")
                        ui.AddWarning(ui.ViewLocalizer[
                            "Brave supports BTCPay Server Vault, but you need to disable Brave Shields. (<a class=\"alert-link\" href=\"https://www.updateland.com/how-to-turn-off-brave-shields/\" target=\"_blank\" rel=\"noreferrer noopener\">More information</a>)"]);
                    ui.ShowRetry();
                    return;
                }

                await Run(ui, client, cancellationToken);
            }
            catch (VaultClient.VaultNotConnectedException)
            {
                ui.ShowFeedback(new VaultElement.Feedback(ui.ViewLocalizer["BTCPay Server Vault does not seem to be running, you can download it on <a target=\"_blank\" href=\"https://github.com/btcpayserver/BTCPayServer.Vault/releases/latest\">Github</a>."], VaultElement.Feedback.StateValue.Failed));
                ui.ShowRetry();
            }
        }
    }
}
