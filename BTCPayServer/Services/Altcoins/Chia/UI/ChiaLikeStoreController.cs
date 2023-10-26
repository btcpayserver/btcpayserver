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
using BTCPayServer.Common.Altcoins.Chia.RPC.Models;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services.Altcoins.Chia.Configuration;
using BTCPayServer.Services.Altcoins.Chia.Payments;
using BTCPayServer.Services.Altcoins.Chia.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Services.Altcoins.Chia.UI
{
    [Route("stores/{storeId}/Chialike")]
    [OnlyIfSupportAttribute("XCH")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UIChiaLikeStoreController : Controller
    {
        private readonly ChiaLikeConfiguration _ChiaLikeConfiguration;
        private readonly StoreRepository _StoreRepository;
        private readonly ChiaRPCProvider _ChiaRpcProvider;
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;

        public UIChiaLikeStoreController(ChiaLikeConfiguration ChiaLikeConfiguration,
            StoreRepository storeRepository, ChiaRPCProvider ChiaRpcProvider,
            BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _ChiaLikeConfiguration = ChiaLikeConfiguration;
            _StoreRepository = storeRepository;
            _ChiaRpcProvider = ChiaRpcProvider;
            _BtcPayNetworkProvider = btcPayNetworkProvider;
        }

        public StoreData StoreData => HttpContext.GetStoreData();

        [HttpGet()]
        public async Task<IActionResult> GetStoreChiaLikePaymentMethods()
        {
            return View(await GetVM(StoreData));
        }

        public async Task<ChiaLikePaymentMethodListViewModel> GetVM(StoreData storeData)
        {
            var chia = storeData.GetSupportedPaymentMethods(_BtcPayNetworkProvider)
                .OfType<ChiaSupportedPaymentMethod>();

            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();

            var accountsList = _ChiaLikeConfiguration.ChiaLikeConfigurationItems.ToDictionary(pair => pair.Key,
                pair => GetWallets(pair.Key));

            await Task.WhenAll(accountsList.Values);
            return new ChiaLikePaymentMethodListViewModel()
            {
                Items = _ChiaLikeConfiguration.ChiaLikeConfigurationItems.Select(pair =>
                    GetChiaLikePaymentMethodViewModel(chia, pair.Key, excludeFilters,
                        accountsList[pair.Key].Result))
            };
        }

        private Task<GetWalletsResponse> GetWallets(string cryptoCode)
        {
            try
            {
                if (_ChiaRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary) && summary.WalletAvailable)
                {
                    return Task.FromResult<GetWalletsResponse>(new GetWalletsResponse()
                    {
                        Wallets = new List<GetWalletsResponse.WalletEntry>() { new() { Id = 1 } }
                    });
                    // return _ChiaRpcProvider.WalletRpcClients[cryptoCode]
                    //     .SendCommandAsync<GetWalletsRequest, GetWalletsResponse>("get_wallets",
                    //         new GetWalletsRequest { Type = 0 });
                }
            }
            catch
            {
            }

            return Task.FromResult<GetWalletsResponse>(null);
        }

        private ChiaLikePaymentMethodViewModel GetChiaLikePaymentMethodViewModel(
            IEnumerable<ChiaSupportedPaymentMethod> Chia, string cryptoCode,
            IPaymentFilter excludeFilters, GetWalletsResponse walletsResponse)
        {
            var settings = Chia.SingleOrDefault(method => method.CryptoCode == cryptoCode);
            _ChiaRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary);
            _ChiaLikeConfiguration.ChiaLikeConfigurationItems.TryGetValue(cryptoCode,
                out var configurationItem);
            // var fileAddress = Path.Combine(configurationItem.WalletDirectory, "wallet");
            var accounts = walletsResponse?.Wallets?.Select(account =>
                new SelectListItem(
                    $"{account.Id} - {(string.IsNullOrEmpty(account.Name) ? "No name" : account.Name)}",
                    account.Id.ToString(CultureInfo.InvariantCulture)));
            return new ChiaLikePaymentMethodViewModel()
            {
                // WalletFileFound = System.IO.File.Exists(fileAddress),
                Enabled =
                    settings != null &&
                    !excludeFilters.Match(new PaymentMethodId(cryptoCode, ChiaPaymentType.Instance)),
                Summary = summary,
                CryptoCode = cryptoCode,
                WalletId =
                    settings?.WalletId ??
                    walletsResponse?.Wallets?.FirstOrDefault()?.Id ?? 1,
                Accounts = accounts == null
                    ? null
                    : new SelectList(accounts, nameof(SelectListItem.Value),
                        nameof(SelectListItem.Text))
            };
        }

        [HttpGet("{cryptoCode}")]
        public async Task<IActionResult> GetStoreChiaLikePaymentMethod(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_ChiaLikeConfiguration.ChiaLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            var vm = GetChiaLikePaymentMethodViewModel(StoreData.GetSupportedPaymentMethods(_BtcPayNetworkProvider)
                    .OfType<ChiaSupportedPaymentMethod>(), cryptoCode,
                StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetWallets(cryptoCode));
            return View(nameof(GetStoreChiaLikePaymentMethod), vm);
        }

        [DisableRequestSizeLimit]
        [HttpPost("{cryptoCode}")]
        public async Task<IActionResult> GetStoreChiaLikePaymentMethod(ChiaLikePaymentMethodViewModel viewModel,
            string command, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_ChiaLikeConfiguration.ChiaLikeConfigurationItems.TryGetValue(cryptoCode,
                    out var configurationItem))
            {
                return NotFound();
            }

            // if (command == "add-account")
            // {
            //     try
            //     {
            //         var newAccount = await _ChiaRpcProvider.WalletRpcClients[cryptoCode]
            //             .SendCommandAsync<CreateAccountRequest, CreateAccountResponse>("create_account",
            //                 new CreateAccountRequest() { Label = viewModel.NewAccountLabel });
            //         viewModel.WalletId = newAccount.AccountIndex;
            //     }
            //     catch (Exception)
            //     {
            //         ModelState.AddModelError(nameof(viewModel.WalletId), "Could not create new account.");
            //     }
            // }
            /*else if (command == "upload-wallet")
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
                    if (_ChiaRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary))
                    {
                        if (summary.WalletAvailable)
                        {
                            TempData.SetStatusMessageModel(new StatusMessageModel()
                            {
                                Severity = StatusMessageModel.StatusSeverity.Error,
                                Message = $"There is already an active wallet configured for {cryptoCode}. Replacing it would break any existing invoices"
                            });
                            return RedirectToAction(nameof(GetStoreChiaLikePaymentMethod),
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

                    return RedirectToAction(nameof(GetStoreChiaLikePaymentMethod), new
                    {
                        cryptoCode,
                        StatusMessage = "Wallet files uploaded. If it was valid, the wallet will become available soon"

                    });
                }
            }*/

            if (!ModelState.IsValid)
            {
                var vm = GetChiaLikePaymentMethodViewModel(StoreData
                        .GetSupportedPaymentMethods(_BtcPayNetworkProvider)
                        .OfType<ChiaSupportedPaymentMethod>(), cryptoCode,
                    StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetWallets(cryptoCode));

                vm.Enabled = viewModel.Enabled;
                vm.NewAccountLabel = viewModel.NewAccountLabel;
                vm.WalletId = viewModel.WalletId;
                return View(vm);
            }

            var storeData = StoreData;
            var blob = storeData.GetStoreBlob();
            storeData.SetSupportedPaymentMethod(new ChiaSupportedPaymentMethod()
            {
                WalletId = viewModel.WalletId, CryptoCode = viewModel.CryptoCode
            });

            blob.SetExcluded(new PaymentMethodId(viewModel.CryptoCode, ChiaPaymentType.Instance),
                !viewModel.Enabled);
            storeData.SetStoreBlob(blob);
            await _StoreRepository.UpdateStore(storeData);
            return RedirectToAction("GetStoreChiaLikePaymentMethods",
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

        public class ChiaLikePaymentMethodListViewModel
        {
            public IEnumerable<ChiaLikePaymentMethodViewModel> Items { get; set; }
        }

        public class ChiaLikePaymentMethodViewModel
        {
            public ChiaRPCProvider.ChiaLikeSummary Summary { get; set; }
            public string CryptoCode { get; set; }
            public string NewAccountLabel { get; set; }
            public int WalletId { get; set; }
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
