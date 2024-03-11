using BTCPayServer.Client;
using BTCPayServer.Filters;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Authorization;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System;
using Microsoft.AspNetCore.Mvc;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Data;
using BTCPayServer.Services.Rates;
using BTCPayServer.ModelBinders;
using BTCPayServer.Plugins.BoltcardBalance;
using System.Collections.Specialized;
using BTCPayServer.Client.Models;
using BTCPayServer.NTag424;
using BTCPayServer.Services;
using NBitcoin.DataEncoders;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Org.BouncyCastle.Ocsp;
using System.Security.Claims;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.BoltcardBalance.Controllers;
using BTCPayServer.HostedServices;

namespace BTCPayServer.Plugins.BoltcardTopUp.Controllers
{
    public class UIBoltcardTopUpController : Controller
    {
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly SettingsRepository _settingsRepository;
        private readonly BTCPayServerEnvironment _env;
        private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;
        private readonly RateFetcher _rateFetcher;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly UIBoltcardBalanceController _boltcardBalanceController;
        private readonly PullPaymentHostedService _ppService;

        public UIBoltcardTopUpController(
            ApplicationDbContextFactory dbContextFactory,
            SettingsRepository settingsRepository,
            BTCPayServerEnvironment env,
            BTCPayNetworkJsonSerializerSettings jsonSerializerSettings,
            RateFetcher rateFetcher,
            BTCPayNetworkProvider networkProvider,
            UIBoltcardBalanceController boltcardBalanceController,
            PullPaymentHostedService ppService,
            CurrencyNameTable currencies)
        {
            _dbContextFactory = dbContextFactory;
            _settingsRepository = settingsRepository;
            _env = env;
            _jsonSerializerSettings = jsonSerializerSettings;
            _rateFetcher = rateFetcher;
            _networkProvider = networkProvider;
            _boltcardBalanceController = boltcardBalanceController;
            _ppService = ppService;
            Currencies = currencies;
        }

        public CurrencyNameTable Currencies { get; }

        [HttpGet("~/stores/{storeId}/boltcards/top-up")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        [AutoValidateAntiforgeryToken]
        public async Task<IActionResult> Keypad(string storeId, string currency = null)
        {
            var settings = new PointOfSaleSettings
            {
                Title = "Boltcards Top-Up"
            };
            currency ??= this.HttpContext.GetStoreData().GetStoreBlob().DefaultCurrency;
            var numberFormatInfo = Currencies.GetNumberFormatInfo(currency);
            double step = Math.Pow(10, -numberFormatInfo.CurrencyDecimalDigits);
            //var store = new Data.StoreData();
            //var storeBlob = new StoreBlob();

            return View($"{BoltcardTopUpPlugin.ViewsDirectory}/Keypad.cshtml", new ViewPointOfSaleViewModel
            {
                Title = settings.Title,
                //StoreName = store.StoreName,
                //BrandColor = storeBlob.BrandColor,
                //CssFileId = storeBlob.CssFileId,
                //LogoFileId = storeBlob.LogoFileId,
                Step = step.ToString(CultureInfo.InvariantCulture),
                //ViewType = BTCPayServer.Plugins.PointOfSale.PosViewType.Light,
                //ShowCustomAmount = settings.ShowCustomAmount,
                //ShowDiscount = settings.ShowDiscount,
                //ShowSearch = settings.ShowSearch,
                //ShowCategories = settings.ShowCategories,
                //EnableTips = settings.EnableTips,
                //CurrencyCode = settings.Currency,
                //CurrencySymbol = numberFormatInfo.CurrencySymbol,
                CurrencyCode = currency,
                CurrencyInfo = new ViewPointOfSaleViewModel.CurrencyInfoData
                {
                    CurrencySymbol = string.IsNullOrEmpty(numberFormatInfo.CurrencySymbol) ? settings.Currency : numberFormatInfo.CurrencySymbol,
                    Divisibility = numberFormatInfo.CurrencyDecimalDigits,
                    DecimalSeparator = numberFormatInfo.CurrencyDecimalSeparator,
                    ThousandSeparator = numberFormatInfo.NumberGroupSeparator,
                    Prefixed = new[] { 0, 2 }.Contains(numberFormatInfo.CurrencyPositivePattern),
                    SymbolSpace = new[] { 2, 3 }.Contains(numberFormatInfo.CurrencyPositivePattern)
                },
                //Items = AppService.Parse(settings.Template, false),
                //ButtonText = settings.ButtonText,
                //CustomButtonText = settings.CustomButtonText,
                //CustomTipText = settings.CustomTipText,
                //CustomTipPercentages = settings.CustomTipPercentages,
                //CustomCSSLink = settings.CustomCSSLink,
                //CustomLogoLink = storeBlob.CustomLogo,
                //AppId = "vouchers",
                StoreId = storeId,
                //Description = settings.Description,
                //EmbeddedCSS = settings.EmbeddedCSS,
                //RequiresRefundEmail = settings.RequiresRefundEmail
            });
        }

        [HttpPost("~/stores/{storeId}/boltcards/top-up")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        [AutoValidateAntiforgeryToken]
        public IActionResult Keypad(string storeId,
        [ModelBinder(typeof(InvariantDecimalModelBinder))] decimal amount, string currency)
        {
            return RedirectToAction(nameof(ScanCard),
                new
                {
                    storeId = storeId,
                    amount = amount,
                    currency = currency
                });
        }

        [HttpGet("~/stores/{storeId}/boltcards/top-up/scan")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        [AutoValidateAntiforgeryToken]
        public async Task<IActionResult> ScanCard(string storeId,
            [ModelBinder(typeof(InvariantDecimalModelBinder))] decimal amount, string currency)
        {
            return View($"{BoltcardTopUpPlugin.ViewsDirectory}/ScanCard.cshtml");
        }

        [HttpPost("~/stores/{storeId}/boltcards/top-up/scan")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        public async Task<IActionResult> ScanCard(string storeId,
            [ModelBinder(typeof(InvariantDecimalModelBinder))] decimal amount, string currency, string p, string c)
        {
            //return View($"{BoltcardBalancePlugin.ViewsDirectory}/BalanceView.cshtml", new BoltcardBalance.ViewModels.BalanceViewModel()
            //{
            //    AmountDue = 10000m,
            //    Currency = "SATS",
            //    Transactions = [new() { Date = DateTimeOffset.UtcNow, Balance = -3.0m }, new() { Date = DateTimeOffset.UtcNow, Balance = -5.0m }]
            //});

            var issuerKey = await _settingsRepository.GetIssuerKey(_env);
            var boltData = issuerKey.TryDecrypt(p);
            if (boltData?.Uid is null)
                return NotFound();
            var id = issuerKey.GetId(boltData.Uid);
            var registration = await _dbContextFactory.GetBoltcardRegistration(issuerKey, boltData, true);
            if (registration is null)
                return NotFound();

            var pp = await _ppService.GetPullPayment(registration.PullPaymentId, false);

            var rules = this.HttpContext.GetStoreData().GetStoreBlob().GetRateRules(_networkProvider);
            var rateResult = await _rateFetcher.FetchRate(new Rating.CurrencyPair("BTC", currency), rules, default);
            var cryptoAmount = Math.Round(amount / rateResult.BidAsk.Bid, 11);

            var ppCurrency = pp.GetBlob().Currency;
            rateResult = await _rateFetcher.FetchRate(new Rating.CurrencyPair(ppCurrency, currency), rules, default);
            var ppAmount = Math.Round(amount / rateResult.BidAsk.Bid, Currencies.GetNumberFormatInfo(ppCurrency).CurrencyDecimalDigits);

            using var ctx = _dbContextFactory.CreateContext();
            var payout = new Data.PayoutData()
            {
                Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(20)),
                Date = DateTimeOffset.UtcNow,
                State = PayoutState.Completed,
                PullPaymentDataId = registration.PullPaymentId,
                PaymentMethodId = new PaymentMethodId("BTC", PaymentTypes.LightningLike).ToString(),
                Destination = null,
                StoreDataId = storeId
            };
            var payoutBlob = new PayoutBlob()
            {
                CryptoAmount = -cryptoAmount,
                Amount = -ppAmount,
                Destination = null,
                Metadata = new JObject(),
            };
            payout.SetBlob(payoutBlob, _jsonSerializerSettings);
            await ctx.Payouts.AddAsync(payout);
            await ctx.SaveChangesAsync();
            _boltcardBalanceController.ViewData["NoCancelWizard"] = true;
            return await _boltcardBalanceController.GetBalanceView(registration, p, issuerKey);
        }
     }
}
