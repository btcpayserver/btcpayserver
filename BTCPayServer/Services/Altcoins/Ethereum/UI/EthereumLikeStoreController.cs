#if ALTCOINS
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services.Altcoins.Ethereum.Filters;
using BTCPayServer.Services.Altcoins.Ethereum.Payments;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using Nethereum.HdWallet;
using Nethereum.Hex.HexConvertors.Extensions;

namespace BTCPayServer.Services.Altcoins.Ethereum.UI
{
    [Route("stores/{storeId}/ethlike")]
    [OnlyIfSupportEth]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class EthereumLikeStoreController : Controller
    {
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        public EthereumLikeStoreController(StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
        }

        private StoreData StoreData => HttpContext.GetStoreData();

        [HttpGet()]
        public IActionResult GetStoreEthereumLikePaymentMethods()
        {
            var eth = StoreData.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<EthereumSupportedPaymentMethod>();

            var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
            var ethNetworks = _btcPayNetworkProvider.GetAll().OfType<EthereumBTCPayNetwork>();

            var vm = new ViewEthereumStoreOptionsViewModel() { };

            foreach (var network in ethNetworks)
            {
                var paymentMethodId = new PaymentMethodId(network.CryptoCode, EthereumPaymentType.Instance);
                var matchedPaymentMethod = eth.SingleOrDefault(method =>
                    method.PaymentId == paymentMethodId);
                vm.Items.Add(new ViewEthereumStoreOptionItemViewModel()
                {
                    CryptoCode = network.CryptoCode,
                    Enabled = matchedPaymentMethod != null && !excludeFilters.Match(paymentMethodId),
                    IsToken = network is ERC20BTCPayNetwork,
                    RootAddress = matchedPaymentMethod?.GetWalletDerivator()?.Invoke(0) ?? "not configured"
                });
            }

            return View(vm);
        }

        [HttpGet("{cryptoCode}")]
        public IActionResult GetStoreEthereumLikePaymentMethod(string cryptoCode)
        {
            var eth = StoreData.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<EthereumSupportedPaymentMethod>();

            var network = _btcPayNetworkProvider.GetNetwork<EthereumBTCPayNetwork>(cryptoCode);
            if (network is null)
            {
                return NotFound();
            }

            var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
            var paymentMethodId = new PaymentMethodId(network.CryptoCode, EthereumPaymentType.Instance);
            var matchedPaymentMethod = eth.SingleOrDefault(method =>
                method.PaymentId == paymentMethodId);

            return View(new EditEthereumPaymentMethodViewModel()
            {
                Enabled = !excludeFilters.Match(paymentMethodId),
                XPub = matchedPaymentMethod?.XPub,
                Index = matchedPaymentMethod?.CurrentIndex ?? 0,
                Passphrase = matchedPaymentMethod?.Password,
                Seed = matchedPaymentMethod?.Seed,
                StoreSeed = !string.IsNullOrEmpty(matchedPaymentMethod?.Seed),
                OriginalIndex = matchedPaymentMethod?.CurrentIndex ?? 0,
                KeyPath = string.IsNullOrEmpty(matchedPaymentMethod?.KeyPath)
                    ? network.GetDefaultKeyPath()
                    : matchedPaymentMethod?.KeyPath
            });
        }

        [HttpPost("{cryptoCode}")]
        public async Task<IActionResult> GetStoreEthereumLikePaymentMethod(string cryptoCode,
            EditEthereumPaymentMethodViewModel viewModel)
        {
            var network = _btcPayNetworkProvider.GetNetwork<EthereumBTCPayNetwork>(cryptoCode);
            if (network is null)
            {
                return NotFound();
            }

            var store = StoreData;
            var blob = StoreData.GetStoreBlob();
            var paymentMethodId = new PaymentMethodId(network.CryptoCode, EthereumPaymentType.Instance);

            var currentPaymentMethod = StoreData.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<EthereumSupportedPaymentMethod>().SingleOrDefault(method =>
                    method.PaymentId == paymentMethodId);

            if (currentPaymentMethod != null && currentPaymentMethod.CurrentIndex != viewModel.Index &&
                viewModel.OriginalIndex == viewModel.Index)
            {
                viewModel.Index = currentPaymentMethod.CurrentIndex;
                viewModel.OriginalIndex = currentPaymentMethod.CurrentIndex;
            }
            else if (currentPaymentMethod != null && currentPaymentMethod.CurrentIndex != viewModel.Index &&
                     viewModel.OriginalIndex != currentPaymentMethod.CurrentIndex)
            {
                ModelState.AddModelError(nameof(viewModel.Index),
                    $"You tried to update the index (to {viewModel.Index}) but new derivations in the background updated the index (to {currentPaymentMethod.CurrentIndex}) ");
                viewModel.Index = currentPaymentMethod.CurrentIndex;
                viewModel.OriginalIndex = currentPaymentMethod.CurrentIndex;
            }

            Wallet wallet = null;
            try
            {
                if (!string.IsNullOrEmpty(viewModel.Seed))
                {
                    wallet = new Wallet(viewModel.Seed, viewModel.Passphrase,
                        string.IsNullOrEmpty(viewModel.KeyPath) ? network.GetDefaultKeyPath() : viewModel.KeyPath);
                }
            }
            catch (Exception)
            {
                ModelState.AddModelError(nameof(viewModel.Seed), $"seed was incorrect");
            }

            if (wallet != null)
            {
                try
                {
                    wallet.GetAccount(0);
                }
                catch (Exception)
                {
                    ModelState.AddModelError(nameof(viewModel.KeyPath), $"keypath was incorrect");
                }
            }

            PublicWallet publicWallet = null;
            try
            {
                if (!string.IsNullOrEmpty(viewModel.XPub))
                {
                    try
                    {
                        publicWallet = new PublicWallet(viewModel.XPub);
                    }
                    catch (Exception)
                    {
                        publicWallet = new PublicWallet(new BitcoinExtPubKey(viewModel.XPub, Network.Main).ExtPubKey);
                    }

                    if (wallet != null && !publicWallet.ExtPubKey.Equals(wallet.GetMasterPublicWallet().ExtPubKey))
                    {
                        ModelState.AddModelError(nameof(viewModel.XPub),
                            $"The xpub does not match the seed/pass/key path provided");
                    }
                }
            }
            catch (Exception)
            {
                ModelState.AddModelError(nameof(viewModel.XPub), $"xpub was incorrect");
            }

            if (!string.IsNullOrEmpty(viewModel.AddressCheck))
            {
                int index = -1;
                if (wallet != null)
                {
                    index = Array.IndexOf(wallet.GetAddresses(1000), viewModel.AddressCheck);
                }
                else if (publicWallet != null)
                {
                    index = Array.IndexOf(publicWallet.GetAddresses(1000), viewModel.AddressCheck);
                }

                if (viewModel.AddressCheckLastUsed && index > -1)
                {
                    viewModel.Index = index;
                }

                if (index == -1)
                {
                    ModelState.AddModelError(nameof(viewModel.AddressCheck),
                        "Could not confirm address belongs to configured wallet");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            currentPaymentMethod ??= new EthereumSupportedPaymentMethod();
            currentPaymentMethod.Password = viewModel.StoreSeed ? viewModel.Passphrase : "";
            currentPaymentMethod.Seed = viewModel.StoreSeed ? viewModel.Seed : "";
            currentPaymentMethod.XPub = string.IsNullOrEmpty(viewModel.XPub) && wallet != null
                ? wallet.GetMasterPublicWallet().ExtPubKey.ToBytes().ToHex()
                : viewModel.XPub;
            currentPaymentMethod.CryptoCode = cryptoCode;
            currentPaymentMethod.KeyPath = string.IsNullOrEmpty(viewModel.KeyPath)
                ? network.GetDefaultKeyPath()
                : viewModel.KeyPath;
            currentPaymentMethod.CurrentIndex = viewModel.Index;

            blob.SetExcluded(paymentMethodId, !viewModel.Enabled);
            store.SetSupportedPaymentMethod(currentPaymentMethod);
            store.SetStoreBlob(blob);
            await _storeRepository.UpdateStore(store);

            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"updated {cryptoCode}", Severity = StatusMessageModel.StatusSeverity.Success
            });

            return RedirectToAction("GetStoreEthereumLikePaymentMethods", new {storeId = store.Id});
        }
    }

    public class EditEthereumPaymentMethodViewModel
    {
        public string XPub { get; set; }
        public string Seed { get; set; }
        public string Passphrase { get; set; }

        public string KeyPath { get; set; }
        public long OriginalIndex { get; set; }

        [Display(Name = "Current address index")]

        public long Index { get; set; }

        public bool Enabled { get; set; }

        [Display(Name = "Hot wallet")] public bool StoreSeed { get; set; }

        [Display(Name ="Address Check")]
        public string AddressCheck { get; set; }

        public bool AddressCheckLastUsed { get; set; }
    }

    public class ViewEthereumStoreOptionsViewModel
    {
        public List<ViewEthereumStoreOptionItemViewModel> Items { get; set; } =
            new List<ViewEthereumStoreOptionItemViewModel>();
    }

    public class ViewEthereumStoreOptionItemViewModel
    {
        public string CryptoCode { get; set; }
        public bool IsToken { get; set; }
        public bool Enabled { get; set; }
        public string RootAddress { get; set; }
    }
}
#endif
