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
using BTCPayServer.Services.Altcoins.Monero.Configuration;
using BTCPayServer.Services.Altcoins.Monero.Payments;
using BTCPayServer.Services.Altcoins.Monero.RPC.Models;
using BTCPayServer.Services.Altcoins.Monero.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Services.Altcoins.Monero.UI
{
    [Route("stores/{storeId}/monerolike")]
    [OnlyIfSupportAttribute("XMR")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UIMoneroLikeStoreController : Controller
    {
        private readonly MoneroLikeConfiguration _MoneroLikeConfiguration;
        private readonly StoreRepository _StoreRepository;
        private readonly MoneroRPCProvider _MoneroRpcProvider;
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;

        public UIMoneroLikeStoreController(MoneroLikeConfiguration moneroLikeConfiguration,
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
            return View(await GetVM(StoreData));
        }
[NonAction]
        public async Task<MoneroLikePaymentMethodListViewModel> GetVM(StoreData storeData)
        {
            var monero = storeData.GetSupportedPaymentMethods(_BtcPayNetworkProvider)
                .OfType<MoneroSupportedPaymentMethod>();

            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();

            var accountsList = _MoneroLikeConfiguration.MoneroLikeConfigurationItems.ToDictionary(pair => pair.Key,
                pair => GetAccounts(pair.Key));

            await Task.WhenAll(accountsList.Values);
            return new MoneroLikePaymentMethodListViewModel()
            {
                Items = _MoneroLikeConfiguration.MoneroLikeConfigurationItems.Select(pair =>
                    GetMoneroLikePaymentMethodViewModel(monero, pair.Key, excludeFilters,
                        accountsList[pair.Key].Result))
            };
        }

        private Task<GetAccountsResponse> GetAccounts(string cryptoCode)
        {
            try
            {
                if (_MoneroRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary) && summary.WalletAvailable)
                {

                    return _MoneroRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<GetAccountsRequest, GetAccountsResponse>("get_accounts", new GetAccountsRequest());
                }
            }
            catch { }
            return Task.FromResult<GetAccountsResponse>(null);
        }

        private MoneroLikePaymentMethodViewModel GetMoneroLikePaymentMethodViewModel(
            IEnumerable<MoneroSupportedPaymentMethod> monero, string cryptoCode,
            IPaymentFilter excludeFilters, GetAccountsResponse accountsResponse)
        {
            var settings = monero.SingleOrDefault(method => method.CryptoCode == cryptoCode);
            _MoneroRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary);
            _MoneroLikeConfiguration.MoneroLikeConfigurationItems.TryGetValue(cryptoCode,
                out var configurationItem);
            var fileAddress = Path.Combine(configurationItem.WalletDirectory, "wallet");
            var accounts = accountsResponse?.SubaddressAccounts?.Select(account =>
                new SelectListItem(
                    $"{account.AccountIndex} - {(string.IsNullOrEmpty(account.Label) ? "No label" : account.Label)}",
                    account.AccountIndex.ToString(CultureInfo.InvariantCulture)));
            return new MoneroLikePaymentMethodViewModel()
            {
                WalletFileFound = System.IO.File.Exists(fileAddress),
                Enabled =
                    settings != null &&
                    !excludeFilters.Match(new PaymentMethodId(cryptoCode, MoneroPaymentType.Instance)),
                Summary = summary,
                CryptoCode = cryptoCode,
                AccountIndex = settings?.AccountIndex ?? accountsResponse?.SubaddressAccounts?.FirstOrDefault()?.AccountIndex ?? 0,
                Accounts = accounts == null ? null : new SelectList(accounts, nameof(SelectListItem.Value),
                    nameof(SelectListItem.Text))
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

            var vm = GetMoneroLikePaymentMethodViewModel(StoreData.GetSupportedPaymentMethods(_BtcPayNetworkProvider)
                    .OfType<MoneroSupportedPaymentMethod>(), cryptoCode,
                StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));
            return View(nameof(GetStoreMoneroLikePaymentMethod), vm);
        }

        [HttpPost("{cryptoCode}")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> GetStoreMoneroLikePaymentMethod(MoneroLikePaymentMethodViewModel viewModel, string command, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_MoneroLikeConfiguration.MoneroLikeConfigurationItems.TryGetValue(cryptoCode,
                out var configurationItem))
            {
                return NotFound();
            }

            if (command == "add-account")
            {
                try
                {
                    var newAccount = await _MoneroRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<CreateAccountRequest, CreateAccountResponse>("create_account", new CreateAccountRequest()
                    {
                        Label = viewModel.NewAccountLabel
                    });
                    viewModel.AccountIndex = newAccount.AccountIndex;
                }
                catch (Exception)
                {
                    ModelState.AddModelError(nameof(viewModel.AccountIndex), "Could not create a new account.");
                }

            }
            else if (command == "upload-wallet")
            {
                var valid = true;
                if (viewModel.WalletFile == null)
                {
                    ModelState.AddModelError(nameof(viewModel.WalletFile), "Please select the view-only wallet file");
                    valid = false;
                }
                if (viewModel.WalletKeysFile == null)
                {
                    ModelState.AddModelError(nameof(viewModel.WalletKeysFile), "Please select the view-only wallet keys file");
                    valid = false;
                }

                if (valid)
                {
                    if (_MoneroRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary))
                    {
                        if (summary.WalletAvailable)
                        {
                            TempData.SetStatusMessageModel(new StatusMessageModel()
                            {
                                Severity = StatusMessageModel.StatusSeverity.Error,
                                Message = $"There is already an active wallet configured for {cryptoCode}. Replacing it would break any existing invoices!"
                            });
                            return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod),
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

                    return RedirectToAction(nameof(GetStoreMoneroLikePaymentMethod), new
                    {
                        cryptoCode,
                        StatusMessage = "View-only wallet files uploaded. If they are valid the wallet will soon become available."

                    });
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
            var blob = storeData.GetStoreBlob();
            storeData.SetSupportedPaymentMethod(new MoneroSupportedPaymentMethod()
            {
                AccountIndex = viewModel.AccountIndex,
                CryptoCode = viewModel.CryptoCode
            });

            blob.SetExcluded(new PaymentMethodId(viewModel.CryptoCode, MoneroPaymentType.Instance), !viewModel.Enabled);
            storeData.SetStoreBlob(blob);
            await _StoreRepository.UpdateStore(storeData);
            return RedirectToAction("GetStoreMoneroLikePaymentMethods",
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

        public class MoneroLikePaymentMethodListViewModel
        {
            public IEnumerable<MoneroLikePaymentMethodViewModel> Items { get; set; }
        }

        public class MoneroLikePaymentMethodViewModel
        {
            public MoneroRPCProvider.MoneroLikeSummary Summary { get; set; }
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
