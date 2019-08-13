using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Monero.RPC.Models;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Monero;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Controllers
{
    [Route("stores/{storeId}/monerolike")]
    [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
    [Authorize(Policy = Policies.CanModifyStoreSettings.Key, AuthenticationSchemes = Policies.CookieAuthentication)]
    [Authorize(Policy = Policies.CanModifyServerSettings.Key, AuthenticationSchemes = Policies.CookieAuthentication)]
    public class MoneroLikeStoreController : Controller
    {
        private readonly MoneroLikeConfiguration _MoneroLikeConfiguration;
        private readonly StoreRepository _StoreRepository;
        private readonly MoneroRPCProvider _MoneroRpcProvider;
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;

        public MoneroLikeStoreController(MoneroLikeConfiguration moneroLikeConfiguration,
            StoreRepository storeRepository, MoneroRPCProvider moneroRpcProvider,
            BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _MoneroLikeConfiguration = moneroLikeConfiguration;
            _StoreRepository = storeRepository;
            _MoneroRpcProvider = moneroRpcProvider;
            _BtcPayNetworkProvider = btcPayNetworkProvider;
        }

        public StoreData StoreData => HttpContext.GetStoreData();

        [HttpGet()]
        public async Task<IActionResult> GetStoreMoneroLikePaymentMethods()
        {
            var monero = StoreData.GetSupportedPaymentMethods(_BtcPayNetworkProvider)
                .OfType<MoneroSupportedPaymentMethod>();

            var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();

            var accountsList = _MoneroLikeConfiguration.MoneroLikeConfigurationItems.ToDictionary(pair => pair.Key,
                pair => GetAccounts(pair.Key));

            await Task.WhenAll(accountsList.Values);

            return View(new MoneroLikePaymentMethodListViewModel()
            {
                Items = _MoneroLikeConfiguration.MoneroLikeConfigurationItems.Select(pair =>
                    GetMoneroLikePaymentMethodViewModel(monero, pair.Key, excludeFilters,
                        accountsList[pair.Key].Result))
            });
        }

        private Task<GetAccountsResponse> GetAccounts(string cryptoCode)
        {
            try
            {
                if (_MoneroRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary) && summary.WalletAvailable)
                {
                    
                    return _MoneroRpcProvider.WalletRpcClients[cryptoCode].GetAccounts(new GetAccountsRequest());
                }
            }catch{}
            return Task.FromResult<GetAccountsResponse>(null);
        }

        private MoneroLikePaymentMethodViewModel GetMoneroLikePaymentMethodViewModel(
            IEnumerable<MoneroSupportedPaymentMethod> monero, string cryptoCode,
            IPaymentFilter excludeFilters, GetAccountsResponse accountsResponse)
        {
            var settings = monero.SingleOrDefault(method => method.CryptoCode == cryptoCode);
            _MoneroRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary);
            return new MoneroLikePaymentMethodViewModel()
            {
                Enabled =
                    settings != null &&
                    !excludeFilters.Match(new PaymentMethodId(cryptoCode, MoneroPaymentType.Instance)),
                Summary = summary,
                CryptoCode = cryptoCode,
                AccountIndex = settings?.AccountIndex ?? 0,
                Accounts = accountsResponse?.SubaddressAccounts.Select(account =>
                    new SelectListItem(
                        $"{account.AccountIndex} - {(string.IsNullOrEmpty(account.Label)? "No label": account.Label)}",
                        account.AccountIndex.ToString(CultureInfo.InvariantCulture)))
            };
        }

        [HttpGet("{cryptoCode}")]
        public async Task<IActionResult> GetStoreMoneroLikePaymentMethod(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            return View(GetMoneroLikePaymentMethodViewModel(StoreData.GetSupportedPaymentMethods(_BtcPayNetworkProvider)
                    .OfType<MoneroSupportedPaymentMethod>(), cryptoCode,
                StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode)));
        }
        
        [HttpPost("{cryptoCode}")]
        public async Task<IActionResult> GetStoreMoneroLikePaymentMethod(MoneroLikePaymentMethodViewModel viewModel, string command, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            if (command == "add-account")
            {
                try
                {
                    var newAccount = await _MoneroRpcProvider.WalletRpcClients[cryptoCode].CreateAccount(new CreateAccountRequest()
                    {
                        Label = viewModel.NewAccountLabel
                    });
                    viewModel.AccountIndex = newAccount.AccountIndex;
                }
                catch (Exception e)
                {
                    ModelState.AddModelError(nameof(viewModel.AccountIndex), "Could not create new account.");
                }
                
            }
            
            if (!ModelState.IsValid)
            {
                
                var vm = GetMoneroLikePaymentMethodViewModel(StoreData
                        .GetSupportedPaymentMethods(_BtcPayNetworkProvider)
                        .OfType<MoneroSupportedPaymentMethod>(), cryptoCode,
                    StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));

                vm.Enabled = viewModel.Enabled;
                vm.NewAccountLabel = viewModel.NewAccountLabel;
                vm.AccountIndex = viewModel.AccountIndex;
                return View(vm);
            }

            var storeData = StoreData;
            storeData.SetSupportedPaymentMethod(viewModel);
            var blob = storeData.GetStoreBlob();
            blob.SetExcluded(viewModel.PaymentId, !viewModel.Enabled);
            storeData.SetStoreBlob(blob);
            await _StoreRepository.UpdateStore(storeData);
            return RedirectToAction("GetStoreMoneroLikePaymentMethods",
                new {StatusMessage = $"{cryptoCode} settings updated successfully", storeId = StoreData.Id});
        }

        public class MoneroLikePaymentMethodListViewModel
        {
            public string StatusMessage { get; set; }
            public IEnumerable<MoneroLikePaymentMethodViewModel> Items { get; set; }
        }

        public class MoneroLikePaymentMethodViewModel : MoneroSupportedPaymentMethod
        {
            public MoneroRPCProvider.MoneroLikeSummary Summary { get; set; }
            public string CryptoCode { get; set; }
            public string NewAccountLabel { get; set; }
            public bool Enabled { get; set; }

            public IEnumerable<SelectListItem> Accounts { get; set; }
        }
    }
}
