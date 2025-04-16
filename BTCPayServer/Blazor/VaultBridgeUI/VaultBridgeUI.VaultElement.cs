using System;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Hwi;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using NBitcoin;

namespace BTCPayServer.Blazor;

public partial class VaultBridgeUI
{
    public class VaultElement
    {
        public class VerifyAddress : VaultElement
        {
            private VaultBridgeUI ui;
            private readonly HwiDeviceClient _device;
            private readonly KeyPath _keyPath;
            private readonly BitcoinAddress _address;
            private readonly ScriptPubKeyType _scriptPubKeyType;

            public VerifyAddress(VaultBridgeUI ui, HwiDeviceClient device, KeyPath keyPath, BitcoinAddress address, ScriptPubKeyType scriptPubKeyType)
            {
                this.ui = ui;
                _device = device;
                _keyPath = keyPath;
                _address = address;
                _scriptPubKeyType = scriptPubKeyType;
            }
            public bool ConfirmedOnDevice { get; set; }
            
            public async Task<bool> WaitConfirmed()
            {
                ui.ShowFeedback(VaultBridgeUI.VaultElement.Feedback.StateValue.Loading, ui.ViewLocalizer["Please verify that the address displayed on your device is <b>{0}</b>...", _address.ToString()]);
                ui.AddElement(this);

                var deviceAddress = await _device.DisplayAddressAsync(_scriptPubKeyType, _keyPath, ui.CancellationToken);
                // Note that the device returned here may be different from what on screen for Testnet/Regtest
                if (deviceAddress != _address)
                {
                    ui.ShowFeedback(VaultBridgeUI.VaultElement.Feedback.StateValue.Failed, ui.StringLocalizer["Unexpected address returned by the device..."]);
                    return false;
                }
                
                ConfirmedOnDevice = true;
                ui.StateHasChanged();
                
                _cts = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return await _cts.Task;
            }
            
            private TaskCompletionSource<bool> _cts;

            public void OnConfirm()
            {
                ui.Elements.Remove(this);
                ui.ShowFeedback(VaultElement.Feedback.StateValue.Success, ui.StringLocalizer["Address verified."]);
                _cts?.TrySetResult(true);
                _cts = null;
            }
            
        }
        public class PinInput : VaultElement, IDisposable
        {
            public int Value => int.TryParse(input, CultureInfo.InvariantCulture, out var v) ? v : 0;
            public string input;
            public string Display { get; set; } = "";

            public void Click(int i)
            {
                input += i;
                Display += "*";
            }
            private VaultBridgeUI ui;
            public PinInput(VaultBridgeUI ui)
            {
                this.ui = ui;
            }
            public Task<int> GetPin()
            {
                ui.ShowFeedback(VaultElement.Feedback.StateValue.Loading, ui.StringLocalizer["Enter the pin."]);
                ui.AddElement(this);
                _cts = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _cts.Task;
            }

            
            private TaskCompletionSource<int> _cts;

            public void OnConfirmPinClick()
            {
                ui.Elements.Remove(this);
                ui.ShowFeedback(VaultElement.Feedback.StateValue.Loading, ui.StringLocalizer["Verifying pin..."]);
                _cts?.TrySetResult(this.Value);
                _cts = null;
            }
            public void Dispose() => _cts?.TrySetCanceled();
        }

        public class Passphrase : VaultElement, IDisposable
        {
            private VaultBridgeUI ui;
            private TaskCompletionSource<string> _cts;

            public Passphrase(VaultBridgeUI ui)
            {
                this.ui = ui;
            }
            public string PasswordConfirmation { get; set; } = "";
            public string Password { get; set; } = "";
            public string Error { get; set; } = "";
            
            public Task<string> GetPassword()
            {
                ui.ShowFeedback(VaultElement.Feedback.StateValue.Loading, ui.StringLocalizer["Enter the passphrase."]);
                ui.AddElement(this);
                _cts = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _cts.Task;
            }
            public void OnConfirmPasswordClick()
            {
                if (Password != PasswordConfirmation)
                {
                    Error = ui.StringLocalizer["Invalid password confirmation."].Value;
                    return;
                }
                
                ui.Elements.Remove(this);
                ui.ShowFeedback(VaultElement.Feedback.StateValue.Success, ui.StringLocalizer["Password entered..."]);

                _cts?.TrySetResult(Password);
                _cts = null;
            }
            public void Dispose() => _cts?.TrySetCanceled();
        }

        public class Retry : VaultElement
        {
        }

        public class XPubSelect : VaultElement, IDisposable
        {
            private readonly VaultBridgeUI ui;
            public XPubSelect(VaultBridgeUI ui, Network network)
            {
                this.ui = ui;
                CanUseTaproot = network.Consensus.SupportTaproot;
                CanUseSegwit = network.Consensus.SupportSegwit;
                AddressType = CanUseSegwit ? "segwit" : "legacy";
            }

            public KeyPath ToKeyPath()
                => ToScriptPubKeyType() switch
                {
                    ScriptPubKeyType.TaprootBIP86 => new KeyPath("86'"),
                    ScriptPubKeyType.Segwit => new KeyPath("84'"),
                    ScriptPubKeyType.SegwitP2SH => new KeyPath("49'"),
                    _ => new KeyPath("44'"),
                };

            public ScriptPubKeyType ToScriptPubKeyType()
                => AddressType switch
                {
                    "segwit" => ScriptPubKeyType.Segwit,
                    "segwitWrapped" => ScriptPubKeyType.SegwitP2SH,
                    "taproot" => ScriptPubKeyType.TaprootBIP86,
                    _ => ScriptPubKeyType.Legacy
                };

            public string AddressType { get; set; }
            public int AccountNumber { get; set; }
            public bool CanUseTaproot { get; }
            public bool CanUseSegwit { get; }
            TaskCompletionSource<XPubSelect> _cts;
            
            public Task<VaultElement.XPubSelect> GetXPubSelect()
            {
                ui.ShowFeedback(VaultElement.Feedback.StateValue.Loading, ui.StringLocalizer["Select your address type and account"]);
                ui.AddElement(this);
                _cts =  new TaskCompletionSource<VaultElement.XPubSelect>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _cts.Task;
            }
            public void OnConfirmXPubClick()
            {
                ui.Elements.Remove(this);
                ui.Elements.RemoveAt(ui.Elements.Count - 1);
                ui.StateHasChanged();
                _cts?.TrySetResult(this);
                _cts = null;
            }
            public void Dispose() => _cts?.TrySetCanceled();
        }

        public class Warning : VaultElement
        {
            public Warning(LocalizedHtmlString str)
            {
                Html = str.Value;
            }

            public string Html { get; set; }
        }

        public class Feedback : VaultElement
        {
            public Feedback()
            {
            }

            public Feedback(LocalizedString str, StateValue state)
            {
                this.State = state;
                this.Text = str.ToString();
            }

            public Feedback(LocalizedHtmlString str, StateValue state)
            {
                this.State = state;
                var txt = new StringWriter();
                str.WriteTo(txt, NullHtmlEncoder.Default);
                this.Html = txt.ToString();
            }

            public enum StateValue
            {
                Loading,
                Success,
                Failed
            }

            public StateValue State { get; set; }

            public string GetClass()
                => State switch
                {
                    StateValue.Loading => "icon-dots feedback-icon-loading",
                    StateValue.Success => "icon-checkmark feedback-icon-success",
                    StateValue.Failed => "icon-cross feedback-icon-failed",
                    _ => ""
                };

            public string GetSymbol()
                => State switch
                {
                    StateValue.Loading => "dots",
                    StateValue.Success => "checkmark",
                    StateValue.Failed => "cross",
                    _ => ""
                };

            public string Html { get; set; }
            public string Text { get; set; }
        }
    }
}
