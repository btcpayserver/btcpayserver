using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models.CustodianAccountViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Services.Custodian.Client;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json.Linq;
using CustodianAccountData = BTCPayServer.Data.CustodianAccountData;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    [ExperimentalRouteAttribute]
    public class UICustodianAccountsController : Controller
    {
        private readonly IEnumerable<ICustodian> _custodianRegistry;
        private readonly CustodianAccountRepository _custodianAccountRepository;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly BTCPayServerClient _btcPayServerClient;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly LinkGenerator _linkGenerator;

        public UICustodianAccountsController(
            CurrencyNameTable currencyNameTable,
            UserManager<ApplicationUser> userManager,
            CustodianAccountRepository custodianAccountRepository,
            IEnumerable<ICustodian> custodianRegistry,
            BTCPayServerClient btcPayServerClient,
            BTCPayNetworkProvider networkProvider,
            LinkGenerator linkGenerator
        )
        {
            _currencyNameTable = currencyNameTable ?? throw new ArgumentNullException(nameof(currencyNameTable));
            _custodianAccountRepository = custodianAccountRepository;
            _custodianRegistry = custodianRegistry;
            _btcPayServerClient = btcPayServerClient;
            _networkProvider = networkProvider;
            _linkGenerator = linkGenerator;
        }

        public string CreatedCustodianAccountId { get; set; }

        [HttpGet("/stores/{storeId}/custodian-accounts/{accountId}")]
        public async Task<IActionResult> ViewCustodianAccount(string storeId, string accountId)
        {
            var custodianAccount = await _custodianAccountRepository.FindById(storeId, accountId);

            if (custodianAccount == null)
                return NotFound();

            var custodian = _custodianRegistry.GetCustodianByCode(custodianAccount.CustodianCode);
            if (custodian == null)
            {
                // TODO The custodian account is broken. The custodian is no longer available. Maybe delete the custodian account?
                return NotFound();
            }

            var vm = new ViewCustodianAccountViewModel();
            vm.Custodian = custodian;
            vm.CustodianAccount = custodianAccount;

            return View(vm);
        }

        [HttpGet("/stores/{storeId}/custodian-accounts/{accountId}.json")]
        public async Task<IActionResult> ViewCustodianAccountJson(string storeId, string accountId)
        {
            var vm = new ViewCustodianAccountBalancesViewModel();
            var custodianAccount = await _custodianAccountRepository.FindById(storeId, accountId);

            if (custodianAccount == null)
                return NotFound();

            var custodian = _custodianRegistry.GetCustodianByCode(custodianAccount.CustodianCode);
            if (custodian == null)
            {
                // TODO The custodian account is broken. The custodian is no longer available. Maybe delete the custodian account?
                return NotFound();
            }

            var store = GetCurrentStore();
            var storeBlob = StoreDataExtensions.GetStoreBlob(store);
            var defaultCurrency = storeBlob.DefaultCurrency;
            vm.StoreId = store.Id;
            vm.DustThresholdInFiat = 1;
            vm.StoreDefaultFiat = defaultCurrency;
            try
            {
                var assetBalances = new Dictionary<string, AssetBalanceInfo>();
                var assetBalancesData =
                    await custodian.GetAssetBalancesAsync(custodianAccount.GetBlob(), cancellationToken: default);

                foreach (var pair in assetBalancesData)
                {
                    var asset = pair.Key;

                    assetBalances.Add(asset,
                        new AssetBalanceInfo
                        {
                            Asset = asset,
                            Qty = pair.Value,
                            FormattedQty = pair.Value.ToString(CultureInfo.InvariantCulture)
                        }
                    );
                }

                if (custodian is ICanTrade tradingCustodian)
                {
                    var config = custodianAccount.GetBlob();
                    var tradableAssetPairs = tradingCustodian.GetTradableAssetPairs();

                    foreach (var pair in assetBalances)
                    {
                        var asset = pair.Key;
                        var assetBalance = assetBalances[asset];
                        var tradableAssetPairsList =
                            tradableAssetPairs.Where(o => o.AssetBought == asset || o.AssetSold == asset).ToList();
                        var tradableAssetPairsDict =
                            new Dictionary<string, AssetPairData>(tradableAssetPairsList.Count);
                        foreach (var assetPair in tradableAssetPairsList)
                        {
                            tradableAssetPairsDict.Add(assetPair.ToString(), assetPair);
                        }

                        assetBalance.TradableAssetPairs = tradableAssetPairsDict;

                        if (asset.Equals(defaultCurrency))
                        {
                            assetBalance.FormattedFiatValue =
                                _currencyNameTable.DisplayFormatCurrency(pair.Value.Qty, defaultCurrency);
                            assetBalance.FiatValue = pair.Value.Qty;
                        }
                        else
                        {
                            try
                            {
                                var quote = await tradingCustodian.GetQuoteForAssetAsync(defaultCurrency, asset,
                                    config, default);
                                assetBalance.Bid = quote.Bid;
                                assetBalance.Ask = quote.Ask;
                                assetBalance.FormattedBid =
                                    _currencyNameTable.DisplayFormatCurrency(quote.Bid, quote.FromAsset);
                                assetBalance.FormattedAsk =
                                    _currencyNameTable.DisplayFormatCurrency(quote.Ask, quote.FromAsset);
                                assetBalance.FormattedFiatValue =
                                    _currencyNameTable.DisplayFormatCurrency(pair.Value.Qty * quote.Bid,
                                        defaultCurrency);
                                assetBalance.FiatValue = pair.Value.Qty * quote.Bid;
                            }
                            catch (WrongTradingPairException)
                            {
                                // Cannot trade this asset, just ignore
                            }
                        }
                    }
                }

                if (custodian is ICanWithdraw withdrawableCustodian)
                {
                    var withdrawableePaymentMethods = withdrawableCustodian.GetWithdrawablePaymentMethods();
                    foreach (var withdrawableePaymentMethod in withdrawableePaymentMethods)
                    {
                        var withdrawableAsset = withdrawableePaymentMethod.Split("-")[0];
                        if (assetBalances.ContainsKey(withdrawableAsset))
                        {
                            var assetBalance = assetBalances[withdrawableAsset];
                            assetBalance.CanWithdraw = true;
                        }
                    }
                }

                if (custodian is ICanDeposit depositableCustodian)
                {
                    vm.DepositablePaymentMethods = depositableCustodian.GetDepositablePaymentMethods();
                }

                vm.AssetBalances = assetBalances;
            }
            catch (Exception e)
            {
                vm.AssetBalanceExceptionMessage = e.Message;
            }

            return Ok(vm);
        }

        [HttpGet("/stores/{storeId}/custodian-accounts/{accountId}/edit")]
        public async Task<IActionResult> EditCustodianAccount(string storeId, string accountId)
        {
            var custodianAccount = await _custodianAccountRepository.FindById(storeId, accountId);
            if (custodianAccount == null)
                return NotFound();

            var custodian = _custodianRegistry.GetCustodianByCode(custodianAccount.CustodianCode);
            if (custodian == null)
            {
                // TODO The custodian account is broken. The custodian is no longer available. Maybe delete the custodian account?
                return NotFound();
            }

            var configForm = await custodian.GetConfigForm(custodianAccount.GetBlob(), "en-US");

            var vm = new EditCustodianAccountViewModel();
            vm.CustodianAccount = custodianAccount;
            vm.ConfigForm = configForm;
            return View(vm);
        }

        [HttpPost("/stores/{storeId}/custodian-accounts/{accountId}/edit")]
        public async Task<IActionResult> EditCustodianAccount(string storeId, string accountId,
            EditCustodianAccountViewModel vm)
        {
            // The locale is not important yet, but keeping it here so we can find it easily when localization becomes a thing.
            var locale = "en-US";

            var custodianAccount = await _custodianAccountRepository.FindById(storeId, accountId);
            if (custodianAccount == null)
                return NotFound();

            var custodian = _custodianRegistry.GetCustodianByCode(custodianAccount.CustodianCode);
            if (custodian == null)
            {
                // TODO The custodian account is broken. The custodian is no longer available. Maybe delete the custodian account?
                return NotFound();
            }

            var configForm = await custodian.GetConfigForm(custodianAccount.GetBlob(), locale);

            var newData = new JObject();
            foreach (var pair in Request.Form)
            {
                if ("CustodianAccount.Name".Equals(pair.Key))
                {
                    custodianAccount.Name = pair.Value;
                }
                else
                {
                    // TODO support posted array notation, like a field called "WithdrawToAddressNamePerPaymentMethod[BTC-OnChain]". The data should be nested in the JSON.
                    newData.Add(pair.Key, pair.Value.ToString());
                }
            }

            var newConfigData = RemoveUnusedFieldsFromConfig(custodianAccount.GetBlob(), newData, configForm);
            var newConfigForm = await custodian.GetConfigForm(newConfigData, locale);

            if (newConfigForm.IsValid())
            {
                custodianAccount.SetBlob(newConfigData);
                custodianAccount = await _custodianAccountRepository.CreateOrUpdate(custodianAccount);

                return RedirectToAction(nameof(ViewCustodianAccount),
                    new { storeId = custodianAccount.StoreId, accountId = custodianAccount.Id });
            }

            // Form not valid: The user must fix the errors before we can save
            vm.CustodianAccount = custodianAccount;
            vm.ConfigForm = newConfigForm;
            return View(vm);
        }


        [HttpGet("/stores/{storeId}/custodian-accounts/create")]
        public IActionResult CreateCustodianAccount(string storeId)
        {
            var vm = new CreateCustodianAccountViewModel();
            vm.StoreId = storeId;
            vm.SetCustodianRegistry(_custodianRegistry);
            return View(vm);
        }

        [HttpPost("/stores/{storeId}/custodian-accounts/create")]
        public async Task<IActionResult> CreateCustodianAccount(string storeId, CreateCustodianAccountViewModel vm)
        {
            var store = GetCurrentStore();
            vm.StoreId = store.Id;
            vm.SetCustodianRegistry(_custodianRegistry);

            var custodian = _custodianRegistry.GetCustodianByCode(vm.SelectedCustodian);
            if (custodian == null)
            {
                ModelState.AddModelError(nameof(vm.SelectedCustodian), "Invalid Custodian");
                return View(vm);
            }

            if (string.IsNullOrEmpty(vm.Name))
            {
                vm.Name = custodian.Name;
            }

            var custodianAccountData = new CustodianAccountData
            {
                CustodianCode = vm.SelectedCustodian, StoreId = vm.StoreId, Name = custodian.Name
            };


            var configData = new JObject();
            foreach (var pair in Request.Form)
            {
                configData.Add(pair.Key, pair.Value.ToString());
            }

            var configForm = await custodian.GetConfigForm(configData, "en-US");
            if (configForm.IsValid())
            {
                // configForm.removeUnusedKeys();
                custodianAccountData.SetBlob(configData);
                custodianAccountData = await _custodianAccountRepository.CreateOrUpdate(custodianAccountData);
                TempData[WellKnownTempData.SuccessMessage] = "Custodian account successfully created";
                CreatedCustodianAccountId = custodianAccountData.Id;

                return RedirectToAction(nameof(ViewCustodianAccount),
                    new { storeId = custodianAccountData.StoreId, accountId = custodianAccountData.Id });
            }

            // Ask for more data
            vm.ConfigForm = configForm;
            return View(vm);
        }

        [HttpPost("/stores/{storeId}/custodian-accounts/{accountId}/delete")]
        public async Task<IActionResult> DeleteCustodianAccount(string storeId, string accountId)
        {
            var custodianAccount = await _custodianAccountRepository.FindById(storeId, accountId);
            if (custodianAccount == null)
            {
                return NotFound();
            }

            var isDeleted = await _custodianAccountRepository.Remove(custodianAccount.Id, custodianAccount.StoreId);
            if (isDeleted)
            {
                TempData[WellKnownTempData.SuccessMessage] = "Custodian account deleted";
                return RedirectToAction(nameof(UIStoresController.Dashboard), "UIStores", new { storeId });
            }

            TempData[WellKnownTempData.ErrorMessage] = "Could not delete custodian account";
            return RedirectToAction(nameof(ViewCustodianAccount),
                new { storeId = custodianAccount.StoreId, accountId = custodianAccount.Id });
        }

        // The JObject may contain too much data because we used ALL post values and this may be more than we needed.
        // Because we don't know the form fields beforehand, we will filter out the superfluous data afterwards.
        // We will keep all the old keys + merge the new keys as per the current form.
        // Since the form can differ by circumstances, we will never remove any keys that were previously stored. We just limit what we add.
        private JObject RemoveUnusedFieldsFromConfig(JObject storedData, JObject newData, Form form)
        {
            JObject filteredData = new JObject();
            var storedKeys = new List<string>();
            foreach (var item in storedData)
            {
                storedKeys.Add(item.Key);
            }

            var formKeys = form.GetAllNames();

            foreach (var item in newData)
            {
                if (storedKeys.Contains(item.Key) || formKeys.Contains(item.Key))
                {
                    filteredData[item.Key] = item.Value;
                }
            }

            return filteredData;
        }

        [HttpGet("/stores/{storeId}/custodian-accounts/{accountId}/trade/prepare")]
        public async Task<IActionResult> GetTradePrepareJson(string storeId, string accountId,
            [FromQuery] string assetToTrade, [FromQuery] string assetToTradeInto)
        {
            if (string.IsNullOrEmpty(assetToTrade) || string.IsNullOrEmpty(assetToTradeInto))
            {
                return BadRequest();
            }

            TradePrepareViewModel vm = new();
            var custodianAccount = await _custodianAccountRepository.FindById(storeId, accountId);

            if (custodianAccount == null)
                return NotFound();

            var custodian = _custodianRegistry.GetCustodianByCode(custodianAccount.CustodianCode);
            if (custodian == null)
            {
                // TODO The custodian account is broken. The custodian is no longer available. Maybe delete the custodian account?
                return NotFound();
            }

            var store = GetCurrentStore();
            var storeBlob = BTCPayServer.Data.StoreDataExtensions.GetStoreBlob(store);
            var defaultCurrency = storeBlob.DefaultCurrency;

            try
            {
                var assetBalancesData =
                    await custodian.GetAssetBalancesAsync(custodianAccount.GetBlob(), cancellationToken: default);

                if (custodian is ICanTrade tradingCustodian)
                {
                    var config = custodianAccount.GetBlob();

                    foreach (var pair in assetBalancesData)
                    {
                        var oneAsset = pair.Key;
                        if (assetToTrade.Equals(oneAsset))
                        {
                            vm.MaxQtyToTrade = pair.Value;
                            //vm.FormattedMaxQtyToTrade = pair.Value;

                            if (assetToTrade.Equals(assetToTradeInto))
                            {
                                // We cannot trade the asset for itself
                                return BadRequest();
                            }

                            try
                            {
                                var quote = await tradingCustodian.GetQuoteForAssetAsync(assetToTrade, assetToTradeInto,
                                    config, default);

                                // TODO Ask is normally a higher number than Bid!! Let's check this!! Maybe a Unit Test?
                                vm.Ask = quote.Ask;
                                vm.Bid = quote.Bid;
                                vm.FromAsset = quote.FromAsset;
                                vm.ToAsset = quote.ToAsset;
                            }
                            catch (WrongTradingPairException)
                            {
                                // Cannot trade this asset, just ignore
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                return BadRequest();
            }

            return Ok(vm);
        }

        [HttpPost("/stores/{storeId}/custodian-accounts/{accountId}/trade")]
        public async Task<IActionResult> Trade(string storeId, string accountId,
            [FromBody] TradeRequestData request)
        {
            try
            {
                var result = await _btcPayServerClient.MarketTradeCustodianAccountAsset(storeId, accountId, request);
                return Ok(result);
            }
            catch (GreenfieldAPIException e)
            {
                var result = new ObjectResult(e.APIError) { StatusCode = e.HttpCode };
                return result;
            }
        }

        [HttpGet("/stores/{storeId}/custodian-accounts/{accountId}/deposit/prepare")]
        public async Task<IActionResult> GetDepositPrepareJson(string storeId, string accountId,
            [FromQuery] string paymentMethod)
        {
            if (string.IsNullOrEmpty(paymentMethod))
            {
                return BadRequest();
            }

            DepositPrepareViewModel vm = new();
            var custodianAccount = await _custodianAccountRepository.FindById(storeId, accountId);

            if (custodianAccount == null)
                return NotFound();

            var custodian = _custodianRegistry.GetCustodianByCode(custodianAccount.CustodianCode);
            if (custodian == null)
            {
                // TODO The custodian account is broken. The custodian is no longer available. Maybe delete the custodian account?
                return NotFound();
            }

            try
            {
                if (custodian is ICanDeposit depositableCustodian)
                {
                    var config = custodianAccount.GetBlob();

                    vm.PaymentMethod = paymentMethod;
                    var depositablePaymentMethods = depositableCustodian.GetDepositablePaymentMethods();
                    if (!depositablePaymentMethods.Contains(paymentMethod))
                    {
                        vm.ErrorMessage = $"Payment method \"{paymentMethod}\" is not supported by {custodian.Name}";
                        return BadRequest(vm);
                    }

                    try
                    {
                        var depositAddressResult =
                            await depositableCustodian.GetDepositAddressAsync(paymentMethod, config, default);
                        vm.Address = depositAddressResult.Address;

                        var paymentMethodObj = PaymentMethodId.Parse(paymentMethod);
                        if (paymentMethodObj.IsBTCOnChain)
                        {
                            var network = _networkProvider.GetNetwork<BTCPayNetwork>("BTC");
                            var bip21 = network.GenerateBIP21(depositAddressResult.Address, null);
                            vm.Link = bip21.ToString();
                            var paymentMethodId = PaymentMethodId.TryParse(paymentMethod);
                            if (paymentMethodId != null)
                            {
                                var walletId = new WalletId(storeId, paymentMethodId.CryptoCode);
                                var returnUrl = _linkGenerator.GetUriByAction(
                                    nameof(ViewCustodianAccount),
                                    "UICustodianAccounts",
                                    new { storeId = custodianAccount.StoreId, accountId = custodianAccount.Id },
                                    Request.Scheme,
                                    Request.Host,
                                    Request.PathBase);

                                vm.CryptoImageUrl = GetImage(paymentMethodId, network);
                                vm.CreateTransactionUrl = _linkGenerator.GetUriByAction(
                                    nameof(UIWalletsController.WalletSend),
                                    "UIWallets",
                                    new { walletId, defaultDestination = vm.Address, returnUrl },
                                    Request.Scheme,
                                    Request.Host,
                                    Request.PathBase);
                            }
                        }
                        else
                        {
                            // TODO support LN + shitcoins
                        }
                    }
                    catch (Exception e)
                    {
                        vm.ErrorMessage = e.Message;
                        return new ObjectResult(vm) { StatusCode = 500 };
                    }
                }
            }
            catch (Exception)
            {
                return BadRequest();
            }

            return Ok(vm);
        }

        private string GetImage(PaymentMethodId paymentMethodId, BTCPayNetwork network)
        {
            // TODO this method was copy-pasted from BTCPayServer.Controllers.UIWalletsController.GetImage(). Maybe refactor this?
            var res = paymentMethodId.PaymentType == PaymentTypes.BTCLike
                ? Url.Content(network.CryptoImagePath)
                : Url.Content(network.LightningImagePath);
            return Request.GetRelativePathOrAbsolute(res);
        }

        private StoreData GetCurrentStore() => HttpContext.GetStoreData();
    }
}
