using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Blazor.VaultBridge.Elements;
using BTCPayServer.Data;
using BTCPayServer.Hwi;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Blazor.VaultBridge;

public abstract class HWIController : VaultController
{
    protected override string VaultUri => "http://127.0.0.1:65092/hwi-bridge/v1";
    public string CryptoCode { get; set; }

    private static bool IsTrezorT(HwiEnumerateEntry deviceEntry)
    {
        return deviceEntry.Model.Contains("Trezor_T", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrezorOne(HwiEnumerateEntry deviceEntry)
    {
        return deviceEntry.Model.Contains("trezor_1", StringComparison.OrdinalIgnoreCase);
    }

    protected abstract Task Run(VaultBridgeUI ui, HwiClient hwi, HwiDeviceClient device, HDFingerprint fingerprint, BTCPayNetwork network,
        CancellationToken cancellationToken);

    protected override async Task Run(VaultBridgeUI ui, VaultClient vaultClient, CancellationToken cancellationToken)
    {
        var networkProviders = ui.ServiceProvider.GetRequiredService<BTCPayNetworkProvider>();
        try
        {
            var network = networkProviders.GetNetwork<BTCPayNetwork>(CryptoCode);
            var hwi = new HwiClient(network.NBitcoinNetwork)
            {
                Transport = new VaultHWITransport(vaultClient), IgnoreInvalidNetwork = network.NBitcoinNetwork.ChainName != ChainName.Mainnet
            };
            ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Fetching device..."]);
            var version = await hwi.GetVersionAsync(cancellationToken);
            if (version.Major < 2)
            {
                ui.ShowFeedback(FeedbackType.Failed,
                    ui.ViewLocalizer[
                        "Your BTCPay Server Vault version is outdated. Please <a target=\"_blank\" href=\"https://github.com/btcpayserver/BTCPayServer.Vault/releases/latest\">download</a> the latest version."]);
            }

            var gettingEntries = hwi.EnumerateEntriesAsync(cancellationToken);
            var timeout = Task.Delay(TimeSpan.FromSeconds(7.0), cancellationToken);
            var finished = await Task.WhenAny(gettingEntries, timeout);
            // Wallets such as Trezor Safe 3 will block EnumerateEntriesAsync until password is set on the device.
            // So if we wait for 7 sec and this doesn't returns, let's notify the user to look the hardware.
            if (finished == timeout)
                ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Please, enter the passphrase on the device."]);
            var entries = await gettingEntries;
            var deviceEntry = entries.FirstOrDefault();

            if (deviceEntry is null)
            {
                ui.ShowFeedback(FeedbackType.Failed, ui.StringLocalizer["No device connected."]);
                ui.ShowRetry();
                return;
            }

            if (deviceEntry.Model is null)
            {
                ui.ShowFeedback(FeedbackType.Failed,
                    ui.StringLocalizer["Unsupported hardware wallet, try to update BTCPay Server Vault"]);
                ui.ShowRetry();
                return;
            }

            var device = new HwiDeviceClient(hwi, deviceEntry.DeviceSelector, deviceEntry.Model, deviceEntry.Fingerprint);
            ui.ShowFeedback(FeedbackType.Success, ui.StringLocalizer["Device found: {0}", device.GetNiceModelName()]);


            HDFingerprint? fingerprint = deviceEntry.Fingerprint;
            bool dirtyDevice = false;
            if (deviceEntry is { Code: HwiErrorCode.DeviceNotReady })
            {
                // It seems that this 'if (IsTrezorT(deviceEntry))' can be removed.
                // I have not managed to trigger this anymore with latest 2.8.9
                // the passphrase is getting asked during EnumerateEntriesAsync
                if (IsTrezorT(deviceEntry))
                {
                    ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Please, enter the passphrase on the device."]);
                    // The make the trezor T ask for password
                    await device.GetXPubAsync(new KeyPath("44'"), cancellationToken);
                    dirtyDevice = true;
                }
                else if (deviceEntry.NeedsPinSent is true)
                {
                    await device.PromptPinAsync(cancellationToken);
                    var pinElement = new PinInput(ui);
                    var pin = await pinElement.GetPin();
                    if (!await device.SendPinAsync(pin, cancellationToken))
                    {
                        ui.ShowFeedback(FeedbackType.Failed, ui.StringLocalizer["Incorrect pin code."]);
                        ui.ShowRetry();
                        return;
                    }

                    ui.ShowFeedback(FeedbackType.Success, ui.StringLocalizer["Pin code verified."]);
                    dirtyDevice = true;
                }
            }
            else if (deviceEntry is { Code: HwiErrorCode.DeviceNotInitialized })
            {
                ui.ShowFeedback(FeedbackType.Failed, ui.StringLocalizer["The device has not been initialized."]);
                ui.ShowRetry();
                return;
            }

            if (IsTrezorOne(deviceEntry) && HasPassphraseProtection(deviceEntry))
            {
                var passwordEl = new Passphrase(ui);
                device.Password = await passwordEl.GetPassword();
                if (!string.IsNullOrEmpty(device.Password))
                {
                    device = new HwiDeviceClient(hwi, DeviceSelectors.FromDeviceType("trezor", deviceEntry.Path), deviceEntry.Model, null)
                    {
                        Password = device.Password
                    };
                    fingerprint = null;
                }
            }

            if (dirtyDevice)
            {
                entries = (await hwi.EnumerateEntriesAsync(cancellationToken)).ToList();
                deviceEntry = entries.FirstOrDefault() ?? deviceEntry;
                device = new HwiDeviceClient(hwi, deviceEntry.DeviceSelector, deviceEntry.Model, deviceEntry.Fingerprint);
                fingerprint = deviceEntry.Fingerprint;
            }

            if (fingerprint is null)
            {
                ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Fetching wallet's fingerprint."]);
                fingerprint = (await device.GetXPubAsync(new KeyPath("44'"), ui.CancellationToken)).ExtPubKey.ParentFingerprint;
                device = new HwiDeviceClient(hwi, DeviceSelectors.FromFingerprint(fingerprint.Value), deviceEntry.Model, fingerprint)
                {
                    Password = device.Password
                };
                ui.ShowFeedback(FeedbackType.Success, ui.StringLocalizer["Wallet's fingerprint fetched."]);
            }

            await Run(ui, hwi, device, fingerprint.Value, network, cancellationToken);
        }
        catch (HwiException e)
        {
            var message = e switch
            {
                { ErrorCode: HwiErrorCode.ActionCanceled } => ui.StringLocalizer["Action canceled by user"],

                //https://github.com/btcpayserver/BTCPayServer.Vault/issues/88
                { ErrorCode: HwiErrorCode.BadArgument } when e.Message.StartsWith("Failed to extract input_tx")
                    => ui.StringLocalizer["The hardware wallet requires previous transactions in the PSBT. Please go to your wallet settings and enable \"Include non-witness UTXO in PSBTs\", then try sending again."],

                _ => ui.StringLocalizer["An unexpected error happened: {0}", $"{e.Message} ({e.ErrorCode})"],
            };
            ui.ShowFeedback(FeedbackType.Failed, message);
            ui.ShowRetry();
        }
    }

    private bool HasPassphraseProtection(HwiEnumerateEntry deviceEntry)
    {
        if (deviceEntry.NeedsPassphraseSent is true)
            return true;
        if (deviceEntry.RawData["warnings"] is JArray arr)
        {
            return arr.Any(e => e.ToString().Contains("passphrase was not provided", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }
}

public class SignHWIController : HWIController
{
    public string StoreId { get; set; }
    /// <summary>
    /// We use byte[] to avoid wasted bytes and hitting size limits of Blazor
    /// </summary>
    public byte[] PSBT { get; set; }

    protected override async Task Run(VaultBridgeUI ui, HwiClient hwi, HwiDeviceClient device, HDFingerprint fingerprint, BTCPayNetwork network,
        CancellationToken cancellationToken)
    {
        if (!NBitcoin.PSBT.TryParse(Convert.ToBase64String(PSBT), network.NBitcoinNetwork, out var psbt))
            return;
        var store = await ui.ServiceProvider.GetRequiredService<StoreRepository>().FindStore(StoreId ?? "");
        var handlers = ui.ServiceProvider.GetRequiredService<PaymentMethodHandlerDictionary>();
        var pmi = Payments.PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
        var derivationSettings = store?.GetPaymentMethodConfig<DerivationSchemeSettings>(pmi, handlers);
        if (store is null || derivationSettings is null)
            return;
        ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Checking if this device can sign the transaction..."]);
        // we ensure that the device fingerprint is part of the derivation settings
        if (derivationSettings.AccountKeySettings.All(a => a.RootFingerprint != fingerprint))
        {
            ui.ShowFeedback(FeedbackType.Failed,
                ui.StringLocalizer[
                    "This device can't sign the transaction. (Wrong device, wrong passphrase or wrong device fingerprint in your wallet settings)"]);
            ui.ShowRetry();
            return;
        }
        derivationSettings.RebaseKeyPaths(psbt);
        // otherwise, let the device check if it can sign anything
        var signableInputs = psbt.Inputs
            .SelectMany(i => i.HDKeyPaths)
            .Where(i => i.Value.MasterFingerprint == fingerprint)
            .ToArray();
        if (signableInputs.Length > 0)
        {
            var actualPubKey = (await device.GetXPubAsync(signableInputs[0].Value.KeyPath, cancellationToken)).GetPublicKey();
            if (actualPubKey != signableInputs[0].Key)
            {
                ui.ShowFeedback(FeedbackType.Failed,
                    ui.StringLocalizer["This device can't sign the transaction. (The wallet keypath in your wallet settings seems incorrect)"]);
                ui.ShowRetry();
                return;
            }

            if (derivationSettings.IsMultiSigOnServer)
            {
                var alreadySigned = psbt.Inputs.Any(a =>
                    a.PartialSigs.Any(o => o.Key == actualPubKey));
                if (alreadySigned)
                {
                    ui.ShowFeedback(FeedbackType.Failed, ui.StringLocalizer["This device already signed PSBT."]);
                    ui.ShowRetry();
                    return;
                }
            }
        }

        ui.ShowFeedback(FeedbackType.Loading,
            ui.StringLocalizer["Please review and confirm the transaction on your device..."]);
        psbt = await device.SignPSBTAsync(psbt, cancellationToken);
        ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Transaction signed successfully, proceeding to review..."]);
        await ui.JSRuntime.InvokeVoidAsync("vault.setSignedPSBT", cancellationToken, new System.Text.Json.Nodes.JsonObject() { ["psbt"] = psbt.ToBase64() });
    }
}

public class GetXPubController : HWIController
{
    protected override async Task Run(VaultBridgeUI ui, HwiClient hwi, HwiDeviceClient device, HDFingerprint fingerprint, BTCPayNetwork network,
        CancellationToken cancellationToken)
    {
        var xpubSelect = new XPubSelect(ui, network.NBitcoinNetwork);
        var xpubInfo = await xpubSelect.GetXPubSelect();
        var scriptPubKeyTypeType = xpubInfo.ToScriptPubKeyType();
        ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Fetching public keys..."]);
        KeyPath keyPath = xpubInfo.ToKeyPath().Derive(network.CoinType).Derive(xpubInfo.AccountNumber, true);
        BitcoinExtPubKey xpub = await device.GetXPubAsync(keyPath, cancellationToken);

        var factory = network.NBXplorerNetwork.DerivationStrategyFactory;
        var strategy = factory.CreateDirectDerivationStrategy(xpub, new DerivationStrategyOptions() { ScriptPubKeyType = scriptPubKeyTypeType });
        ui.ShowFeedback(FeedbackType.Success, ui.StringLocalizer["Public keys successfully fetched."]);

        var firstDepositPath = new KeyPath(0, 0);
        var firstDepositAddr = strategy.GetDerivation(firstDepositPath).ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork);

        var verif = new VerifyAddress(ui)
        {
            Device = device,
            KeyPath = keyPath.Derive(firstDepositPath),
            Address = firstDepositAddr,
            ScriptPubKeyType = xpubInfo.ToScriptPubKeyType()
        };
        if (!await verif.WaitConfirmed())
        {
            ui.ShowRetry();
            return;
        }

        ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Saving..."]);

        var settings = new DerivationSchemeSettings(strategy, network) { Source = "Vault" };
        settings.AccountKeySettings[0].AccountKeyPath = keyPath;
        settings.AccountKeySettings[0].RootFingerprint = fingerprint;

        string[] mandatoryPrevUtxo = ["trezor", "jade"];
        settings.DefaultIncludeNonWitnessUtxo = (device.Model, scriptPubKeyTypeType) switch
        {
            (_, ScriptPubKeyType.TaprootBIP86) => false,
            (_, ScriptPubKeyType.Legacy) => true,
            ({ } s, _) when mandatoryPrevUtxo.Any(o => s.Contains(o, StringComparison.OrdinalIgnoreCase)) => true,
            _ => false,
        };

        settings.Label = $"{device.GetNiceModelName()} ({fingerprint})";

        var handlers = ui.ServiceProvider.GetRequiredService<PaymentMethodHandlerDictionary>();
        var handler = handlers.GetBitcoinHandler(network.CryptoCode);
        var dataProtector = ui.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("ConfigProtector");

        await ui.JSRuntime.InvokeVoidAsync("vault.setXPub", cancellationToken,
            new System.Text.Json.Nodes.JsonObject() { ["config"] = dataProtector.ProtectString(JToken.FromObject(settings, handler.Serializer).ToString()) });
    }
}
