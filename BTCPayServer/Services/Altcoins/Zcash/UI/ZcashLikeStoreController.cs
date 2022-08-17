#if ALTCOINS
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services.Altcoins.Zcash.Configuration;
using BTCPayServer.Services.Altcoins.Zcash.Payments;
using BTCPayServer.Services.Altcoins.Zcash.RPC.Models;
using BTCPayServer.Services.Altcoins.Zcash.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Services.Altcoins.Zcash.UI
{
    [Route("stores/{storeId}/Zcashlike")]
    [OnlyIfSupportAttribute("ZEC")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UIZcashLikeStoreController : Controller
    {
        private readonly ZcashLikeConfiguration _ZcashLikeConfiguration;
        private readonly StoreRepository _StoreRepository;
        private readonly ZcashRPCProvider _ZcashRpcProvider;
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;

        public UIZcashLikeStoreController(ZcashLikeConfiguration ZcashLikeConfiguration,
            StoreRepository storeRepository, ZcashRPCProvider ZcashRpcProvider,
            BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _ZcashLikeConfiguration = ZcashLikeConfiguration;
            _StoreRepository = storeRepository;
            _ZcashRpcProvider = ZcashRpcProvider;
            _BtcPayNetworkProvider = btcPayNetworkProvider;
        }

        public StoreData StoreData => HttpContext.GetStoreData();

        [HttpGet()]
        public async Task<IActionResult> GetStoreZcashLikePaymentMethods()
        {
            var Zcash = StoreData.GetSupportedPaymentMethods(_BtcPayNetworkProvider)
                .OfType<ZcashSupportedPaymentMethod>();

            var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();

            var accountsList = _ZcashLikeConfiguration.ZcashLikeConfigurationItems.ToDictionary(pair => pair.Key,
                pair => GetAccounts(pair.Key));

            await Task.WhenAll(accountsList.Values);
            return View(new ZcashLikePaymentMethodListViewModel()
            {
                Items = _ZcashLikeConfiguration.ZcashLikeConfigurationItems.Select(pair =>
                    GetZcashLikePaymentMethodViewModel(Zcash, pair.Key, excludeFilters,
                        accountsList[pair.Key].Result))
            });
        }

        private Task<GetAccountsResponse> GetAccounts(string cryptoCode)
        {
            try
            {
                if (_ZcashRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary) && summary.WalletAvailable)
                {

                    return _ZcashRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<GetAccountsRequest, GetAccountsResponse>("get_accounts", new GetAccountsRequest());
                }
            }
            catch { }
            return Task.FromResult<GetAccountsResponse>(null);
        }

        private ZcashLikePaymentMethodViewModel GetZcashLikePaymentMethodViewModel(
            IEnumerable<ZcashSupportedPaymentMethod> Zcash, string cryptoCode,
            IPaymentFilter excludeFilters, GetAccountsResponse accountsResponse)
        {
            var settings = Zcash.SingleOrDefault(method => method.CryptoCode == cryptoCode);
            _ZcashRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary);
            _ZcashLikeConfiguration.ZcashLikeConfigurationItems.TryGetValue(cryptoCode,
                out var configurationItem);
            var fileAddress = Path.Combine(configurationItem.WalletDirectory, "wallet");
            var accounts = accountsResponse?.SubaddressAccounts?.Select(account =>
                new SelectListItem(
                    $"{account.AccountIndex} - {(string.IsNullOrEmpty(account.Label) ? "No label" : account.Label)}",
                    account.AccountIndex.ToString(CultureInfo.InvariantCulture)));
            return new ZcashLikePaymentMethodViewModel()
            {
                WalletFileFound = System.IO.File.Exists(fileAddress),
                Enabled =
                    settings != null &&
                    !excludeFilters.Match(new PaymentMethodId(cryptoCode, ZcashPaymentType.Instance)),
                Summary = summary,
                CryptoCode = cryptoCode,
                AccountIndex = settings?.AccountIndex ?? accountsResponse?.SubaddressAccounts?.FirstOrDefault()?.AccountIndex ?? 0,
                Accounts = accounts == null ? null : new SelectList(accounts, nameof(SelectListItem.Value),
                    nameof(SelectListItem.Text))
            };
        }

        [HttpGet("{cryptoCode}")]
        public async Task<IActionResult> GetStoreZcashLikePaymentMethod(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_ZcashLikeConfiguration.ZcashLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            var vm = GetZcashLikePaymentMethodViewModel(StoreData.GetSupportedPaymentMethods(_BtcPayNetworkProvider)
                    .OfType<ZcashSupportedPaymentMethod>(), cryptoCode,
                StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));
            return View(nameof(GetStoreZcashLikePaymentMethod), vm);
        }

        [DisableRequestSizeLimit]
        [HttpPost("{cryptoCode}")]
        public async Task<IActionResult> GetStoreZcashLikePaymentMethod(ZcashLikePaymentMethodViewModel viewModel, string command, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_ZcashLikeConfiguration.ZcashLikeConfigurationItems.TryGetValue(cryptoCode,
                out var configurationItem))
            {
                return NotFound();
            }

            if (command == "add-account")
            {
                try
                {
                    var newAccount = await _ZcashRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<CreateAccountRequest, CreateAccountResponse>("create_account", new CreateAccountRequest()
                    {
                        Label = viewModel.NewAccountLabel
                    });
                    viewModel.AccountIndex = newAccount.AccountIndex;
                }
                catch (Exception)
                {
                    ModelState.AddModelError(nameof(viewModel.AccountIndex), "Could not create new account.");
                }

            }
            else if (command == "upload-wallet")
            {
                var valid = true;
                if (viewModel.WalletFile == null)
                {
                    ModelState.AddModelError(nameof(viewModel.WalletFile), "Please select the wallet file");
                    valid = false;
                }
                if (viewModel.WalletKeysFile == null)
                {
                    ModelState.AddModelError(nameof(viewModel.WalletKeysFile), "Please select the wallet.keys file");
                    valid = false;
                }

                if (valid)
                {
                    if (_ZcashRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary))
                    {
                        if (summary.WalletAvailable)
                        {
                            TempData.SetStatusMessageModel(new StatusMessageModel()
                            {
                                Severity = StatusMessageModel.StatusSeverity.Error,
                                Message = $"There is already an active wallet configured for {cryptoCode}. Replacing it would break any existing invoices"
                            });
                            return RedirectToAction(nameof(GetStoreZcashLikePaymentMethod),
                                new { cryptoCode });
                        }
                    }

                    var fileAddress = Path.Combine(configurationItem.WalletDirectory, "wallet");
                    using (var fileStream = new FileStream(fileAddress, FileMode.Create))
                    {
                        await viewModel.WalletFile.CopyToAsync(fileStream);
                        try
                        {
                            Exec($"chmod 666 {fileAddress}");
                        }
                        catch
                        {
                        }
                    }

                    fileAddress = Path.Combine(configurationItem.WalletDirectory, "wallet.keys");
                    using (var fileStream = new FileStream(fileAddress, FileMode.Create))
                    {
                        await viewModel.WalletKeysFile.CopyToAsync(fileStream);
                        try
                        {
                            Exec($"chmod 666 {fileAddress}");
                        }
                        catch
                        {
                        }

                    }

                    fileAddress = Path.Combine(configurationItem.WalletDirectory, "password");
                    using (var fileStream = new StreamWriter(fileAddress, false))
                    {
                        await fileStream.WriteAsync(viewModel.WalletPassword);
                        try
                        {
                            Exec($"chmod 666 {fileAddress}");
                        }
                        catch
                        {
                        }
                    }

                    return RedirectToAction(nameof(GetStoreZcashLikePaymentMethod), new
                    {
                        cryptoCode,
                        StatusMessage = "Wallet files uploaded. If it was valid, the wallet will become available soon"

                    });
                }
            }

            if (!ModelState.IsValid)
            {

                var vm = GetZcashLikePaymentMethodViewModel(StoreData
                        .GetSupportedPaymentMethods(_BtcPayNetworkProvider)
                        .OfType<ZcashSupportedPaymentMethod>(), cryptoCode,
                    StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));

                vm.Enabled = viewModel.Enabled;
                vm.NewAccountLabel = viewModel.NewAccountLabel;
                vm.AccountIndex = viewModel.AccountIndex;
                return View(vm);
            }

            var storeData = StoreData;
            var blob = storeData.GetStoreBlob();
            storeData.SetSupportedPaymentMethod(new ZcashSupportedPaymentMethod()
            {
                AccountIndex = viewModel.AccountIndex,
                CryptoCode = viewModel.CryptoCode
            });

            blob.SetExcluded(new PaymentMethodId(viewModel.CryptoCode, ZcashPaymentType.Instance), !viewModel.Enabled);
            storeData.SetStoreBlob(blob);
            await _StoreRepository.UpdateStore(storeData);
            return RedirectToAction("GetStoreZcashLikePaymentMethods",
                new { StatusMessage = $"{cryptoCode} settings updated successfully", storeId = StoreData.Id });
        }

        private void Exec(string cmd)
        {

            var escapedArgs = cmd.Replace("\"", "\\\"", StringComparison.InvariantCulture);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = "/bin/sh",
                    Arguments = $"-c \"{escapedArgs}\""
                }
            };

#pragma warning disable CA1416 // Validate platform compatibility
            process.Start();
#pragma warning restore CA1416 // Validate platform compatibility
            process.WaitForExit();
        }

        public class ZcashLikePaymentMethodListViewModel
        {
            public IEnumerable<ZcashLikePaymentMethodViewModel> Items { get; set; }
        }

        public class ZcashLikePaymentMethodViewModel
        {
            public ZcashRPCProvider.ZcashLikeSummary Summary { get; set; }
            public string CryptoCode { get; set; }
            public string NewAccountLabel { get; set; }
            public long AccountIndex { get; set; }
            public bool Enabled { get; set; }

            public IEnumerable<SelectListItem> Accounts { get; set; }
            public bool WalletFileFound { get; set; }
            [Display(Name = "View-Only Wallet File")]
            public IFormFile WalletFile { get; set; }
            public IFormFile WalletKeysFile { get; set; }
            public string WalletPassword { get; set; }
        }
    }
}
#endif
