using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

namespace BTCPayServer.Blazor;

public partial class VaultBridgeUI
{
    public class SignDeviceAction : IDeviceAction
    {
        public string StoreId { get; set; }
        public string PSBT { get; set; }
        public async Task Run(DeviceActionContext ctx, CancellationToken cancellationToken)
        {
            if (!NBitcoin.PSBT.TryParse(PSBT, ctx.Network.NBitcoinNetwork, out var psbt))
                return;
            var store = await ctx.ServiceProvider.GetRequiredService<StoreRepository>().FindStore(StoreId ?? "");
            var handlers = ctx.ServiceProvider.GetRequiredService<PaymentMethodHandlerDictionary>();
            var pmi = Payments.PaymentTypes.CHAIN.GetPaymentMethodId(ctx.Network.CryptoCode);
            var derivationSettings = store?.GetPaymentMethodConfig<DerivationSchemeSettings>(pmi, handlers);
            if (store is null || derivationSettings is null)
                return;
            ctx.UI.ShowFeedback(VaultElement.Feedback.StateValue.Loading, ctx.UI.StringLocalizer["Checking if this device can sign the transaction..."]);
            // we ensure that the device fingerprint is part of the derivation settings
            if (derivationSettings.AccountKeySettings.All(a => a.RootFingerprint != ctx.Fingerprint))
            {
                ctx.UI.ShowFeedback(VaultElement.Feedback.StateValue.Failed, ctx.UI.StringLocalizer["This device can't sign the transaction. (Wrong device, wrong passphrase or wrong device fingerprint in your wallet settings)"]);
                ctx.UI.ShowRetry();
                return;
            }
            derivationSettings.RebaseKeyPaths(psbt);
            // otherwise, let the device check if it can sign anything
            var signableInputs = psbt.Inputs
                .SelectMany(i => i.HDKeyPaths)
                .Where(i => i.Value.MasterFingerprint == ctx.Fingerprint)
                .ToArray();
            if (signableInputs.Length > 0)
            {
                var actualPubKey = (await ctx.Device.GetXPubAsync(signableInputs[0].Value.KeyPath, cancellationToken)).GetPublicKey();
                if (actualPubKey != signableInputs[0].Key)
                {
                    ctx.UI.ShowFeedback(VaultElement.Feedback.StateValue.Failed, ctx.UI.StringLocalizer["This device can't sign the transaction. (The wallet keypath in your wallet settings seems incorrect)"]);
                    ctx.UI.ShowRetry();
                    return;
                }
                
                if (derivationSettings.IsMultiSigOnServer)
                {
                    var alreadySigned = psbt.Inputs.Any(a =>
                        a.PartialSigs.Any(a => a.Key == actualPubKey));
                    if (alreadySigned)
                    {
                        ctx.UI.ShowFeedback(VaultElement.Feedback.StateValue.Failed, ctx.UI.StringLocalizer["This device already signed PSBT."]);
                        ctx.UI.ShowRetry();
                        return;
                    }
                }
            }
            ctx.UI.ShowFeedback(VaultElement.Feedback.StateValue.Loading, ctx.UI.StringLocalizer["Please review and confirm the transaction on your device..."]);
            psbt = await ctx.Device.SignPSBTAsync(psbt, cancellationToken);
            ctx.UI.ShowFeedback(VaultElement.Feedback.StateValue.Loading, ctx.UI.StringLocalizer["Transaction signed successfully, proceeding to review..."]);
            await ctx.JS.InvokeVoidAsync("vault.setSignedPSBT", cancellationToken, new System.Text.Json.Nodes.JsonObject()
            {
                ["psbt"] = psbt.ToBase64()
            });
        }
    }
    public class GetXPubDeviceAction : IDeviceAction
    {
        public async Task Run(DeviceActionContext ctx, CancellationToken cancellationToken)
        {
            var xpubSelect = new VaultElement.XPubSelect(ctx.UI, ctx.Network.NBitcoinNetwork);
            var xpubInfo = await xpubSelect.GetXPubSelect();
            var scriptPubKeyTypeType = xpubInfo.ToScriptPubKeyType();
            ctx.UI.ShowFeedback(VaultElement.Feedback.StateValue.Loading, ctx.UI.StringLocalizer["Fetching public keys..."]);
            KeyPath keyPath = xpubInfo.ToKeyPath().Derive(ctx.Network.CoinType).Derive(xpubInfo.AccountNumber, true);
            BitcoinExtPubKey xpub = await ctx.Device.GetXPubAsync(keyPath, cancellationToken);

            var factory = ctx.Network.NBXplorerNetwork.DerivationStrategyFactory;
            var strategy = factory.CreateDirectDerivationStrategy(xpub, new DerivationStrategyOptions()
            {
                ScriptPubKeyType = scriptPubKeyTypeType
            });
            ctx.UI.ShowFeedback(VaultElement.Feedback.StateValue.Success, ctx.UI.StringLocalizer["Public keys successfully fetched."]);

            var firstDepositPath = new KeyPath(0, 0);
            var firstDepositAddr = ctx.Network.NBXplorerNetwork.CreateAddress(strategy, firstDepositPath,strategy.GetDerivation(firstDepositPath).ScriptPubKey);
            ctx.UI.ShowFeedback(VaultElement.Feedback.StateValue.Loading, ctx.UI.ViewLocalizer["Please verify that the address displayed on your device is <b>{0}</b>...", firstDepositAddr.ToString()]);

            var verif = new VaultElement.VerifyAddress(ctx.UI, ctx.Device, keyPath.Derive(firstDepositPath), firstDepositAddr, xpubInfo.ToScriptPubKeyType());
            if (!await verif.WaitConfirmed())
            {
                ctx.UI.ShowRetry();
                return;
            }
            ctx.UI.ShowFeedback(VaultElement.Feedback.StateValue.Loading, ctx.UI.StringLocalizer["Saving..."]);

            var settings = new DerivationSchemeSettings(strategy, ctx.Network) { Source = "Vault" };
            settings.AccountKeySettings[0].AccountKeyPath = keyPath;
            settings.AccountKeySettings[0].RootFingerprint = ctx.Device.Fingerprint;

            string[] mandatoryPrevUtxo = ["trezor", "jade"];
            settings.DefaultIncludeNonWitnessUtxo = (ctx.Device.Model, scriptPubKeyTypeType) switch
            {
                (_, ScriptPubKeyType.TaprootBIP86) => false,
                (_, ScriptPubKeyType.Legacy) => true,
                ({ } s, _) when mandatoryPrevUtxo.Any(o => s.Contains(o, StringComparison.OrdinalIgnoreCase)) => true,
                _ => false,
            };

            var fp = ctx.Device.Fingerprint is { } f ? $" ({f})" : "";
            settings.Label = $"{ctx.Device.GetNiceModelName()}{fp}"; 

            var handlers = ctx.ServiceProvider.GetRequiredService<PaymentMethodHandlerDictionary>();
            var handler = handlers.GetBitcoinHandler(ctx.Network.CryptoCode);
            var dataProtector = ctx.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("ConfigProtector");
            
            await ctx.JS.InvokeVoidAsync("vault.setXPub", cancellationToken, new System.Text.Json.Nodes.JsonObject()
            {
                ["config"] = dataProtector.ProtectString(JToken.FromObject(settings, handler.Serializer).ToString())
            });
        }
    }
    public async Task ConnectToHWI(VaultClient client)
    {
        try
        {
            var network = NetworkProviders.GetNetwork<BTCPayNetwork>(CryptoCode);
            var hwi = new Hwi.HwiClient(network.NBitcoinNetwork)
            {
                Transport = new VaultHWITransport(client),
                IgnoreInvalidNetwork = network.NBitcoinNetwork.ChainName != ChainName.Mainnet
            };
            this.ShowFeedback(VaultElement.Feedback.StateValue.Loading, StringLocalizer["Fetching device..."]);
            var version = await hwi.GetVersionAsync(CancellationToken);
            if (version.Major < 2)
            {
                ShowFeedback(VaultElement.Feedback.StateValue.Failed, ViewLocalizer["Your BTCPay Server Vault version is outdated. Please <a target=\"_blank\" href=\"https://github.com/btcpayserver/BTCPayServer.Vault/releases/latest\">download</a> the latest version."]);
            }
            
            var gettingEntries = hwi.EnumerateEntriesAsync(CancellationToken);
            var timeout = Task.Delay(TimeSpan.FromSeconds(7.0), CancellationToken);
            var finished = await Task.WhenAny(gettingEntries, timeout);
            // Wallets such as Trezor Safe 3 will block EnumerateEntriesAsync until password is set on the device.
            // So if we wait for 7 sec and this doesn't returns, let's notify the user to look the hardware.
            if (finished == timeout)
                this.ShowFeedback(VaultElement.Feedback.StateValue.Loading, StringLocalizer["Please, enter the passphrase on the device."]);
            var entries = await gettingEntries;
            var deviceEntry = entries.FirstOrDefault();

            if (deviceEntry is null)
            {
                this.ShowFeedback(VaultElement.Feedback.StateValue.Failed, StringLocalizer["No device connected."]);
                ShowRetry();
                return;
            }

            if (deviceEntry.Model is null)
            {
                this.ShowFeedback(VaultElement.Feedback.StateValue.Failed, StringLocalizer["Unsupported hardware wallet, try to update BTCPay Server Vault"]);
                ShowRetry();
                return;
            }

            var device = new HwiDeviceClient(hwi, deviceEntry.DeviceSelector, deviceEntry.Model, deviceEntry.Fingerprint);
            this.ShowFeedback(VaultElement.Feedback.StateValue.Success, StringLocalizer["Device found: {0}", device.GetNiceModelName()]);


            HDFingerprint? fingerprint = deviceEntry.Fingerprint;
            bool dirtyDevice = false;
            if (deviceEntry is { Code: HwiErrorCode.DeviceNotReady })
            {
                // It seems that this 'if (IsTrezorT(deviceEntry))' can be removed.
                // I have not managed to trigger this anymore with latest 2.8.9
                // the passphrase is getting asked during EnumerateEntriesAsync 
                if (IsTrezorT(deviceEntry))
                {
                    this.ShowFeedback(VaultElement.Feedback.StateValue.Loading, StringLocalizer["Please, enter the passphrase on the device."]);
                    // The make the trezor T ask for password
                    await device.GetXPubAsync(new KeyPath("44'"), CancellationToken);
                    dirtyDevice = true;
                }
                else if (deviceEntry.NeedsPinSent is true)
                {
                    await device.PromptPinAsync(CancellationToken);
                    var pinElement = new VaultElement.PinInput(this);
                    var pin = await pinElement.GetPin();
                    if (!await device.SendPinAsync(pin, CancellationToken))
                    {
                        this.ShowFeedback(VaultElement.Feedback.StateValue.Failed, StringLocalizer["Incorrect pin code."]);
                        ShowRetry();
                        return;
                    }

                    this.ShowFeedback(VaultElement.Feedback.StateValue.Success, StringLocalizer["Pin code verified."]);
                    dirtyDevice = true;
                }
                else if (deviceEntry.NeedsPassphraseSent is true)
                {
                    var passwordEl = new VaultElement.Passphrase(this);
                    device.Password = await passwordEl.GetPassword();
                }
            }
            else if (deviceEntry is { Code: HwiErrorCode.DeviceNotInitialized })
            {
                this.ShowFeedback(VaultElement.Feedback.StateValue.Failed, StringLocalizer["The device has not been initialized."]);
                ShowRetry();
                return;
            }

            // For Trezor One, we always ask for the password.
            // If the user doesn't have any, he can just leave empty.
            if (IsTrezorOne(deviceEntry) && device.Password is null)
            {
                var passwordEl = new VaultElement.Passphrase(this);
                device.Password = await passwordEl.GetPassword();
                if (!string.IsNullOrEmpty(device.Password))
                {
                    device = new HwiDeviceClient(hwi, DeviceSelectors.FromDeviceType("trezor", deviceEntry.Path), deviceEntry.Model, null)
                    {
                        Password = device.Password
                    };
                }
            }

            if (!string.IsNullOrEmpty(device.Password))
                fingerprint = null;

            if (dirtyDevice)
            {
                entries = (await hwi.EnumerateEntriesAsync(CancellationToken)).ToList();
                deviceEntry = entries.FirstOrDefault() ?? deviceEntry;
                device = new HwiDeviceClient(hwi, deviceEntry.DeviceSelector, deviceEntry.Model, deviceEntry.Fingerprint) { Password = device.Password };
                fingerprint = deviceEntry.Fingerprint;
            }

            if (fingerprint is null)
            {
                this.ShowFeedback(VaultElement.Feedback.StateValue.Loading, StringLocalizer["Fetching wallet's fingerprint."]);
                fingerprint = (await device.GetXPubAsync(new KeyPath("44'"), CancellationToken)).ExtPubKey.ParentFingerprint;
                device = new HwiDeviceClient(hwi, DeviceSelectors.FromFingerprint(fingerprint.Value), deviceEntry.Model, fingerprint) { Password = device.Password };
                this.ShowFeedback(VaultElement.Feedback.StateValue.Success, StringLocalizer["Wallet's fingerprint fetched."]);
            }

            if (DeviceAction is { } da)
                await da.Run(new(ServiceProvider, this, JSRuntime, hwi, device, fingerprint.Value, network), CancellationToken);
        }
        catch (HwiException e)
        {
            var message = e switch
            {
                { ErrorCode: HwiErrorCode.ActionCanceled } => StringLocalizer["Action canceled by user"],
                _ =>  StringLocalizer["An unexpected error happened: {0}", $"{e.Message} ({e.ErrorCode})"],
            };
            this.ShowFeedback(VaultElement.Feedback.StateValue.Failed, message);
            ShowRetry();
        }
    }
    private static bool IsTrezorT(HwiEnumerateEntry deviceEntry)
    {
        return deviceEntry.Model.Contains("Trezor_T", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrezorOne(HwiEnumerateEntry deviceEntry)
    {
        return deviceEntry.Model.Contains("trezor_1", StringComparison.OrdinalIgnoreCase);
    }
}
