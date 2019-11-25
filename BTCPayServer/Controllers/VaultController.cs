using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Hwi;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services;
using LedgerWallet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers
{
    [Route("vault")]
    public class VaultController : Controller
    {
        private readonly IAuthorizationService _authorizationService;

        public VaultController(BTCPayNetworkProvider networks, IAuthorizationService authorizationService)
        {
            Networks = networks;
            _authorizationService = authorizationService;
        }

        public BTCPayNetworkProvider Networks { get; }

        [HttpGet]
        [Route("{cryptoCode}/xpub")]
        [Route("wallets/{walletId}/xpub")]
        public async Task<IActionResult> VaultBridgeConnection(string cryptoCode = null,
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId = null)
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return NotFound();
            cryptoCode = cryptoCode ?? walletId.CryptoCode;
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
            {
                var cancellationToken = cts.Token;
                var network = Networks.GetNetwork<BTCPayNetwork>(cryptoCode);
                if (network == null)
                    return NotFound();
                var websocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                var hwi = new Hwi.HwiClient(network.NBitcoinNetwork)
                {
                    Transport = new HwiWebSocketTransport(websocket)
                };
                Hwi.HwiDeviceClient device = null;
                HwiEnumerateEntry deviceEntry = null;
                HDFingerprint? fingerprint = null;
                string password = null;
                int? pin = null;
                var websocketHelper = new WebSocketHelper(websocket);

                async Task<bool> RequireDeviceUnlocking()
                {
                    if (deviceEntry == null)
                    {
                        await websocketHelper.Send("{ \"error\": \"need-device\"}", cancellationToken);
                        return true;
                    }
                    if (deviceEntry.Code is HwiErrorCode.DeviceNotInitialized)
                    {
                        await websocketHelper.Send("{ \"error\": \"need-initialized\"}", cancellationToken);
                        return true;
                    }
                    if ((deviceEntry.Code is HwiErrorCode.DeviceNotReady || deviceEntry.NeedsPinSent is true)
                        && pin is null
                        // Trezor T always show the pin on screen
                        && (deviceEntry.Model != HardwareWalletModels.Trezor_T || deviceEntry.Model != HardwareWalletModels.Trezor_T_Simulator))
                    {
                        await websocketHelper.Send("{ \"error\": \"need-pin\"}", cancellationToken);
                        return true;
                    }
                    if ((deviceEntry.Code is HwiErrorCode.DeviceNotReady || deviceEntry.NeedsPassphraseSent is true) && password == null)
                    {
                        await websocketHelper.Send("{ \"error\": \"need-passphrase\"}", cancellationToken);
                        return true;
                    }
                    return false;
                }

                JObject o = null;
                try
                {
                    while (true)
                    {
                        var command = await websocketHelper.NextMessageAsync(cancellationToken);
                        switch (command)
                        {
                            case "set-passphrase":
                                device.Password = await websocketHelper.NextMessageAsync(cancellationToken);
                                password = device.Password;
                                break;
                            case "ask-sign":
                                if (await RequireDeviceUnlocking())
                                {
                                    continue;
                                }
                                if (walletId == null)
                                {
                                    await websocketHelper.Send("{ \"error\": \"invalid-walletId\"}", cancellationToken);
                                    continue;
                                }
                                if (fingerprint is null)
                                {
                                    fingerprint = (await device.GetXPubAsync(new KeyPath("44'"), cancellationToken)).ExtPubKey.ParentFingerprint;
                                }
                                await websocketHelper.Send("{ \"info\": \"ready\"}", cancellationToken);
                                o = JObject.Parse(await websocketHelper.NextMessageAsync(cancellationToken));
                                var authorization = await _authorizationService.AuthorizeAsync(User, Policies.CanModifyStoreSettings.Key);
                                if (!authorization.Succeeded)
                                {
                                    await websocketHelper.Send("{ \"error\": \"not-authorized\"}", cancellationToken);
                                    continue;
                                }
                                var psbt = PSBT.Parse(o["psbt"].Value<string>(), network.NBitcoinNetwork);
                                var derivationSettings = GetDerivationSchemeSettings(walletId);
                                derivationSettings.RebaseKeyPaths(psbt);
                                var signing = derivationSettings.GetSigningAccountKeySettings();
                                if (signing.GetRootedKeyPath()?.MasterFingerprint != fingerprint)
                                {
                                    await websocketHelper.Send("{ \"error\": \"wrong-wallet\"}", cancellationToken);
                                    continue;
                                }
                                try
                                {
                                    psbt = await device.SignPSBTAsync(psbt, cancellationToken);
                                }
                                catch (Hwi.HwiException)
                                {
                                    await websocketHelper.Send("{ \"error\": \"user-reject\"}", cancellationToken);
                                    continue;
                                }
                                o = new JObject();
                                o.Add("psbt", psbt.ToBase64());
                                await websocketHelper.Send(o.ToString(), cancellationToken);
                                break;
                            case "ask-pin":
                                if (device == null)
                                {
                                    await websocketHelper.Send("{ \"error\": \"need-device\"}", cancellationToken);
                                    continue;
                                }
                                await device.PromptPinAsync(cancellationToken);
                                await websocketHelper.Send("{ \"info\": \"prompted, please input the pin\"}", cancellationToken);
                                pin = int.Parse(await websocketHelper.NextMessageAsync(cancellationToken), CultureInfo.InvariantCulture);
                                if (await device.SendPinAsync(pin.Value, cancellationToken))
                                {
                                    await websocketHelper.Send("{ \"info\": \"the pin is correct\"}", cancellationToken);
                                }
                                else
                                {
                                    await websocketHelper.Send("{ \"error\": \"incorrect-pin\"}", cancellationToken);
                                    continue;
                                }
                                break;
                            case "ask-xpubs":
                                if (await RequireDeviceUnlocking())
                                {
                                    continue;
                                }
                                JObject result = new JObject();
                                var factory = network.NBXplorerNetwork.DerivationStrategyFactory;
                                var keyPath = new KeyPath("84'").Derive(network.CoinType).Derive(0, true);
                                BitcoinExtPubKey xpub = await device.GetXPubAsync(keyPath);
                                if (fingerprint is null)
                                {
                                    fingerprint = (await device.GetXPubAsync(new KeyPath("44'"), cancellationToken)).ExtPubKey.ParentFingerprint;
                                }
                                result["fingerprint"] = fingerprint.Value.ToString();
                                var strategy = factory.CreateDirectDerivationStrategy(xpub, new DerivationStrategyOptions()
                                {
                                    ScriptPubKeyType = ScriptPubKeyType.Segwit
                                });
                                AddDerivationSchemeToJson("segwit", result, keyPath, xpub, strategy);
                                keyPath = new KeyPath("49'").Derive(network.CoinType).Derive(0, true);
                                xpub = await device.GetXPubAsync(keyPath);
                                strategy = factory.CreateDirectDerivationStrategy(xpub, new DerivationStrategyOptions()
                                {
                                    ScriptPubKeyType = ScriptPubKeyType.SegwitP2SH
                                });
                                AddDerivationSchemeToJson("segwitWrapped", result, keyPath, xpub, strategy);
                                keyPath = new KeyPath("44'").Derive(network.CoinType).Derive(0, true);
                                xpub = await device.GetXPubAsync(keyPath);
                                strategy = factory.CreateDirectDerivationStrategy(xpub, new DerivationStrategyOptions()
                                {
                                    ScriptPubKeyType = ScriptPubKeyType.Legacy
                                });
                                AddDerivationSchemeToJson("legacy", result, keyPath, xpub, strategy);
                                await websocketHelper.Send(result.ToString(), cancellationToken);
                                break;
                            case "ask-device":
                                password = null;
                                pin = null;
                                deviceEntry = null;
                                device = null;
                                var entries = (await hwi.EnumerateEntriesAsync(cancellationToken)).ToList();
                                deviceEntry = entries.FirstOrDefault();
                                if (deviceEntry == null)
                                {
                                    await websocketHelper.Send("{ \"error\": \"no-device\"}", cancellationToken);
                                    continue;
                                }
                                device = new HwiDeviceClient(hwi, deviceEntry.DeviceSelector, deviceEntry.Model, deviceEntry.Fingerprint);
                                fingerprint = device.Fingerprint;
                                JObject json = new JObject();
                                json.Add("model", device.Model.ToString());
                                json.Add("fingerprint", device.Fingerprint?.ToString());
                                await websocketHelper.Send(json.ToString(), cancellationToken);
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    JObject obj = new JObject();
                    obj.Add("error", "unknown-error");
                    obj.Add("details", ex.ToString());
                    try
                    {
                        await websocketHelper.Send(obj.ToString(), cancellationToken);
                    }
                    catch { }
                }
                finally
                {
                    await websocketHelper.DisposeAsync(cancellationToken);
                }
            }
            return new EmptyResult();
        }

        public StoreData CurrentStore
        {
            get
            {
                return HttpContext.GetStoreData();
            }
        }

        private DerivationSchemeSettings GetDerivationSchemeSettings(WalletId walletId)
        {
            var paymentMethod = CurrentStore
                            .GetSupportedPaymentMethods(Networks)
                            .OfType<DerivationSchemeSettings>()
                            .FirstOrDefault(p => p.PaymentId.PaymentType == Payments.PaymentTypes.BTCLike && p.PaymentId.CryptoCode == walletId.CryptoCode);
            return paymentMethod;
        }

        private void AddDerivationSchemeToJson(string propertyName, JObject result, KeyPath keyPath, BitcoinExtPubKey xpub, DerivationStrategyBase strategy)
        {
            result.Add(new JProperty(propertyName, new JObject()
            {
                new JProperty("strategy", strategy.ToString()),
                new JProperty("accountKey", xpub.ToString()),
                new JProperty("keyPath", keyPath.ToString()),
            }));
        }
    }
}
