using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Plugins.Liquid.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public class StoreLiquidController : Controller
{
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly BTCPayServerClient _client;
    private readonly IExplorerClientProvider _explorerClientProvider;

    public StoreLiquidController(BTCPayNetworkProvider btcPayNetworkProvider,
        BTCPayServerClient client, IExplorerClientProvider explorerClientProvider)
    {
        _btcPayNetworkProvider = btcPayNetworkProvider;
        _client = client;
        _explorerClientProvider = explorerClientProvider;
    }

    [HttpGet("stores/{storeId}/liquid")]
    public async Task<IActionResult> GenerateLiquidScript(string storeId, Dictionary<string, BitcoinExtKey> bitcoinExtKeys = null)
    {
        Dictionary<string, string> generated = new Dictionary<string, string>();
        var allNetworks = _btcPayNetworkProvider.GetAll().OfType<ElementsBTCPayNetwork>()
            .GroupBy(network => network.NetworkCryptoCode);
        var allNetworkCodes = allNetworks
            .SelectMany(networks => networks.Select(network => network.CryptoCode.ToUpperInvariant()))
            .ToArray()
            .Distinct();
        Dictionary<string, BitcoinExtKey> privKeys = bitcoinExtKeys ?? new Dictionary<string, BitcoinExtKey>();
            
            
        var paymentMethods = (await _client.GetStoreOnChainPaymentMethods(storeId))
            .Where(settings => allNetworkCodes.Contains(settings.CryptoCode))
            .GroupBy(data => _btcPayNetworkProvider.GetNetwork<ElementsBTCPayNetwork>(data.CryptoCode).NetworkCryptoCode);

        if (paymentMethods.Any() is false)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Info,
                Message = "There are no wallets configured that use Liquid or an elements side-chain."
            });
            return View(new GenerateLiquidImportScripts());
        }
            
        foreach (var der in paymentMethods)
        {
            var network = _btcPayNetworkProvider.GetNetwork<ElementsBTCPayNetwork>(der.Key);
            var nbxnet = network.NBXplorerNetwork;
               
            var sb = new StringBuilder();

            var explorerClient = _explorerClientProvider.GetExplorerClient(der.Key);
            var status = await explorerClient.GetStatusAsync();
            if (status.BitcoinStatus is null)
            {
                sb.AppendLine($"{der.Key} node is not available. Try again later.");
                generated.Add(der.Key, sb.ToString());
                continue;
            }
            var derivationSchemesForNetwork = der.GroupBy(data => data.DerivationScheme);
                
            foreach (var paymentMethodDerivationScheme in derivationSchemesForNetwork)
            {
                var derivatonScheme =
                    nbxnet.DerivationStrategyFactory.Parse(paymentMethodDerivationScheme.Key);
                var sameWalletCryptoCodes = paymentMethodDerivationScheme.Select(data => data.CryptoCode).ToArray();
                var matchedExistingKey = privKeys.Where(pair => sameWalletCryptoCodes.Contains(pair.Key));
                BitcoinExtKey key = null;
                if (matchedExistingKey.Any())
                {
                    key = matchedExistingKey.First().Value;
                }
                else
                {

                    key = await explorerClient.GetMetadataAsync<BitcoinExtKey>(derivatonScheme,
                        WellknownMetadataKeys.AccountHDKey);
                }

                if (key != null)
                {
                        
                    foreach (var paymentMethodData in paymentMethodDerivationScheme)
                    {
                        privKeys.TryAdd(paymentMethodData.CryptoCode, key);
                    }
                }

                var utxos = await explorerClient.GetUTXOsAsync(derivatonScheme, CancellationToken.None);
                    
                foreach (var utxo in utxos.GetUnspentUTXOs())
                {
                    var addr = nbxnet.CreateAddress(derivatonScheme, utxo.KeyPath, utxo.ScriptPubKey);

                    if (key is null)
                    {
                        sb.AppendLine(
                            $"elements-cli importaddress \"{addr}\" \"{utxo.KeyPath} from {derivatonScheme}\" false");
                    }
                    else
                    {
                        sb.AppendLine(
                            $"elements-cli importprivkey \"{key.Derive(utxo.KeyPath).PrivateKey.GetWif(nbxnet.NBitcoinNetwork)}\" \"{utxo.KeyPath} from {derivatonScheme}\" false");
                    }

                    if (!derivatonScheme.Unblinded())
                    {
                        var blindingKey =
                            NBXplorerNetworkProvider.LiquidNBXplorerNetwork.GenerateBlindingKey(
                                derivatonScheme, utxo.KeyPath, utxo.ScriptPubKey, nbxnet.NBitcoinNetwork);
                        sb.AppendLine($"elements-cli importblindingkey {addr} {blindingKey.ToHex()}");
                    }
                }
            }

            if (sb.Length > 0)
            {
                sb.AppendLine("elements-cli stop");
                sb.AppendLine("elementsd -rescan");
                    
            }
            generated.Add(der.Key, sb.ToString());
        }

        return View(new GenerateLiquidImportScripts()
        {
            Wallets = paymentMethods.SelectMany(settings =>
                settings.Select(data => 
                    new GenerateLiquidImportScripts.GenerateLiquidImportScriptWalletKeyVm()
                    {
                        CryptoCode = data.CryptoCode,
                        KeyPresent = privKeys.ContainsKey(data.CryptoCode),
                        ManualKey = null
                    }).ToArray()).ToArray(),
            Scripts = generated
        });
    }


    [HttpPost("stores/{storeId}/liquid")]
    public async Task<IActionResult> GenerateLiquidScript(string storeId, GenerateLiquidImportScripts vm)
    {
        Dictionary<string, BitcoinExtKey> privKeys = new Dictionary<string, BitcoinExtKey>();
        for (var index = 0; index < vm.Wallets.Length; index++)
        {
            var wallet = vm.Wallets[index];
            if (string.IsNullOrEmpty(wallet.ManualKey))
                continue;

            var n =
                _btcPayNetworkProvider.GetNetwork<ElementsBTCPayNetwork>(wallet.CryptoCode);
            ExtKey extKey = null;
            try
            {
                var mnemonic = new Mnemonic(wallet.ManualKey);
                extKey = mnemonic.DeriveExtKey();
            }
            catch (Exception)
            {
            }

            if (extKey == null)
            {
                try
                {
                    extKey = ExtKey.Parse(wallet.ManualKey, n.NBitcoinNetwork);
                }
                catch (Exception)
                {
                }
            }

            if (extKey == null)
            {
                BTCPayServer.ModelStateExtensions.AddModelError(vm, scripts => scripts.Wallets[index].ManualKey,
                    "Invalid key (must be seed or root xprv or account xprv)", this);
                continue;
            }



            var der = n.NBXplorerNetwork.DerivationStrategyFactory.Parse(
                (await _client.GetStoreOnChainPaymentMethod(storeId, wallet.CryptoCode)).DerivationScheme);
            if (der.GetExtPubKeys().Count() > 1)
            {
                BTCPayServer.ModelStateExtensions.AddModelError(vm, scripts => scripts.Wallets[index].ManualKey, "cannot handle multsig", this);
                continue;
            }

            var first = der
                .GetExtPubKeys().First();
            if (first != extKey.Neuter())
            {
                KeyPath kp = null;
                switch (der.ScriptPubKeyType())
                {
                    case ScriptPubKeyType.Legacy:
                        kp = new KeyPath($"m/44'/{n.CoinType}/0'");
                        break;
                    case ScriptPubKeyType.Segwit:

                        kp = new KeyPath($"m/84'/{n.CoinType}/0'");
                        break;
                    case ScriptPubKeyType.SegwitP2SH:
                        kp = new KeyPath($"m/49'/{n.CoinType}/0'");
                        break;
                    default:
                        BTCPayServer.ModelStateExtensions.AddModelError(vm, scripts => scripts.Wallets[index].ManualKey, "cannot handle wallet type",
                            this);
                        continue;
                }

                extKey = extKey.Derive(kp);
                if (first != extKey.Neuter())
                {
                    BTCPayServer.ModelStateExtensions.AddModelError(vm, scripts => scripts.Wallets[index].ManualKey, "key did not match", this);
                    continue;
                }
            }

            privKeys.TryAdd(wallet.CryptoCode, extKey.GetWif(n.NBitcoinNetwork));
        }

        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        return await GenerateLiquidScript(storeId, privKeys);
    }
    public class GenerateLiquidImportScripts
    {
        public class GenerateLiquidImportScriptWalletKeyVm
        {
            public string CryptoCode { get; set; }
            public bool KeyPresent { get; set; }
            public string ManualKey { get; set; }
        }

        public GenerateLiquidImportScriptWalletKeyVm[] Wallets { get; set; } =
            Array.Empty<GenerateLiquidImportScriptWalletKeyVm>();

        public Dictionary<string, string> Scripts { get; set; } = new Dictionary<string, string>();
    }
}

public static class ModelStateExtensions
{
    public static void AddModelError<TModel, TProperty>(this TModel source,
        Expression<Func<TModel, TProperty>> ex,
        string message,
        ControllerBase controller)
    {
        var provider = (ModelExpressionProvider)controller.HttpContext.RequestServices.GetService(typeof(ModelExpressionProvider));
        var key = provider.GetExpressionText(ex);
        controller.ModelState.AddModelError(key, message);
    }
}
