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
                bool pinProvided = false;
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
                        && !pinProvided)
                    {
                        if (!IsTrezorT(deviceEntry))
                        {
                            await websocketHelper.Send("{ \"error\": \"need-pin\"}", cancellationToken);
                            return true;
                        }
                        else
                        {
                            try
                            {
                                // On trezor T this will prompt the password! (https://github.com/bitcoin-core/HWI/issues/283)
                                _ = device.GetXPubAsync(new KeyPath("44'"), cancellationToken);
                            }
                            catch (HwiException ex) when (ex.ErrorCode == HwiErrorCode.DeviceAlreadyUnlocked)
                            {
                                pinProvided = true;
                            }
                            await websocketHelper.Send("{ \"error\": \"need-passphrase-on-device\"}", cancellationToken);
                            return true;
                        }
                    }
                    if ((deviceEntry.Code is HwiErrorCode.DeviceNotReady || deviceEntry.NeedsPassphraseSent is true) && password == null)
                    {
                        if (IsTrezorT(deviceEntry))
                        {
                            await websocketHelper.Send("{ \"error\": \"need-passphrase-on-device\"}", cancellationToken);
                        }
                        else
                        {
                            await websocketHelper.Send("{ \"error\": \"need-passphrase\"}", cancellationToken);
                        }
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
                                var signableInputs = psbt.Inputs
                                                .SelectMany(i => i.HDKeyPaths)
                                                .Where(i => i.Value.MasterFingerprint == fingerprint)
                                                .ToArray();
                                if (signableInputs.Length > 0)
                                {
                                    var actualPubKey = (await device.GetXPubAsync(signableInputs[0].Value.KeyPath)).GetPublicKey();
                                    if (actualPubKey != signableInputs[0].Key)
                                    {
                                        await websocketHelper.Send("{ \"error\": \"wrong-keypath\"}", cancellationToken);
                                        continue;
                                    }
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
                                try
                                {
                                    await device.PromptPinAsync(cancellationToken);
                                }
                                catch (HwiException ex) when (ex.ErrorCode == HwiErrorCode.DeviceAlreadyUnlocked)
                                {
                                    pinProvided = true;
                                    await websocketHelper.Send("{ \"error\": \"device-already-unlocked\"}", cancellationToken);
                                    continue;
                                }
                                await websocketHelper.Send("{ \"info\": \"prompted, please input the pin\"}", cancellationToken);
                                var pin = int.Parse(await websocketHelper.NextMessageAsync(cancellationToken), CultureInfo.InvariantCulture);
                                if (await device.SendPinAsync(pin, cancellationToken))
                                {
                                    pinProvided = true;
                                    await websocketHelper.Send("{ \"info\": \"the pin is correct\"}", cancellationToken);
                                }
                                else
                                {
                                    await websocketHelper.Send("{ \"error\": \"incorrect-pin\"}", cancellationToken);
                                    continue;
                                }
                                break;
                            case "ask-xpub":
                                if (await RequireDeviceUnlocking())
                                {
                                    continue;
                                }
                                await websocketHelper.Send("{ \"info\": \"ok\"}", cancellationToken);
                                var askedXpub = JObject.Parse(await websocketHelper.NextMessageAsync(cancellationToken));
                                var addressType = askedXpub["addressType"].Value<string>();
                                var accountNumber = askedXpub["accountNumber"].Value<int>();
                                JObject result = new JObject();
                                var factory = network.NBXplorerNetwork.DerivationStrategyFactory;
                                if (fingerprint is null)
                                {
                                    fingerprint = (await device.GetXPubAsync(new KeyPath("44'"), cancellationToken)).ExtPubKey.ParentFingerprint;
                                }
                                result["fingerprint"] = fingerprint.Value.ToString();

                                DerivationStrategyBase strategy = null;
                                KeyPath keyPath = null;
                                BitcoinExtPubKey xpub = null;

                                if (!network.NBitcoinNetwork.Consensus.SupportSegwit && addressType != "legacy")
                                {
                                    await websocketHelper.Send("{ \"error\": \"segwit-notsupported\"}", cancellationToken);
                                    continue;
                                }

                                if (addressType == "segwit")
                                {
                                    keyPath = new KeyPath("84'").Derive(network.CoinType).Derive(accountNumber, true);
                                    xpub = await device.GetXPubAsync(keyPath);
                                    strategy = factory.CreateDirectDerivationStrategy(xpub, new DerivationStrategyOptions()
                                    {
                                        ScriptPubKeyType = ScriptPubKeyType.Segwit
                                    });
                                }
                                else if (addressType == "segwitWrapped")
                                {
                                    keyPath = new KeyPath("49'").Derive(network.CoinType).Derive(accountNumber, true);
                                    xpub = await device.GetXPubAsync(keyPath);
                                    strategy = factory.CreateDirectDerivationStrategy(xpub, new DerivationStrategyOptions()
                                    {
                                        ScriptPubKeyType = ScriptPubKeyType.SegwitP2SH
                                    });
                                }
                                else if (addressType == "legacy")
                                {
                                    keyPath = new KeyPath("44'").Derive(network.CoinType).Derive(accountNumber, true);
                                    xpub = await device.GetXPubAsync(keyPath);
                                    strategy = factory.CreateDirectDerivationStrategy(xpub, new DerivationStrategyOptions()
                                    {
                                        ScriptPubKeyType = ScriptPubKeyType.Legacy
                                    });
                                }
                                else
                                {
                                    await websocketHelper.Send("{ \"error\": \"invalid-addresstype\"}", cancellationToken);
                                    continue;
                                }
                                result.Add(new JProperty("strategy", strategy.ToString()));
                                result.Add(new JProperty("accountKey", xpub.ToString()));
                                result.Add(new JProperty("keyPath", keyPath.ToString()));
                                await websocketHelper.Send(result.ToString(), cancellationToken);
                                break;
                            case "refresh-device":
                            case "ask-device":
                                DeviceSelector deviceSelector = (command == "refresh-device" && deviceEntry != null ? deviceEntry.DeviceSelector : null);
                                password = null;
                                pinProvided = false;
                                deviceEntry = null;
                                device = null;
                                var entries = (await hwi.EnumerateEntriesAsync(cancellationToken)).ToList();
                                deviceEntry = entries.Where(h => deviceSelector == null || SameSelector(deviceSelector, h.DeviceSelector)).FirstOrDefault();
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
                catch (FormatException ex)
                {
                    JObject obj = new JObject();
                    obj.Add("error", "invalid-network");
                    obj.Add("details", ex.ToString());
                    try
                    {
                        await websocketHelper.Send(obj.ToString(), cancellationToken);
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    JObject obj = new JObject();
                    obj.Add("error", "unknown-error");
                    obj.Add("message", ex.Message);
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

        private bool SameSelector(DeviceSelector a, DeviceSelector b)
        {
            var aargs = new List<string>();
            a.AddArgs(aargs);
            var bargs = new List<string>();
            b.AddArgs(bargs);
            if (aargs.Count != bargs.Count)
                return false;
            for (int i = 0; i < aargs.Count; i++)
            {
                if (aargs[i] != bargs[i])
                    return false;
            }
            return true;
        }

        private static bool IsTrezorT(HwiEnumerateEntry deviceEntry)
        {
            return (deviceEntry.Model == HardwareWalletModels.Trezor_T || deviceEntry.Model == HardwareWalletModels.Trezor_T_Simulator);
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
    }
}
