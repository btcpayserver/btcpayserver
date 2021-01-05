using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenFieldStoresController : ControllerBase
    {
        private readonly StoreRepository _storeRepository;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        public GreenFieldStoresController(StoreRepository storeRepository, UserManager<ApplicationUser> userManager, BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _storeRepository = storeRepository;
            _userManager = userManager;
            _btcPayNetworkProvider = btcPayNetworkProvider;
        }
        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores")]
        public ActionResult<IEnumerable<Client.Models.StoreData>> GetStores()
        {
            var stores = HttpContext.GetStoresData();
            return Ok(stores.Select(FromModel));
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}")]
        public ActionResult<Client.Models.StoreData> GetStore(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }
            return Ok(FromModel(store));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}")]
        public async Task<IActionResult> RemoveStore(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }

            if (!_storeRepository.CanDeleteStores())
            {
                return this.CreateAPIError("unsupported",
                    "BTCPay Server is using a database server that does not allow you to remove stores.");
            }
            await _storeRepository.RemoveStore(storeId, _userManager.GetUserId(User));
            return Ok();
        }

        [HttpPost("~/api/v1/stores")]
        [Authorize(Policy = Policies.CanModifyStoreSettingsUnscoped, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateStore(CreateStoreRequest request)
        {
            var validationResult = Validate(request);
            if (validationResult != null)
            {
                return validationResult;
            }

            var store = new Data.StoreData();
            
            PaymentMethodId.TryParse(request.DefaultPaymentMethod, out var defaultPaymnetMethodId);
            ToModel(request, store, defaultPaymnetMethodId);
            await _storeRepository.CreateStore(_userManager.GetUserId(User), store);
            return Ok(FromModel(store));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}")]
        public async Task<IActionResult> UpdateStore(string storeId, UpdateStoreRequest request)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }
            var validationResult = Validate(request);
            if (validationResult != null)
            {
                return validationResult;
            }

            PaymentMethodId.TryParse(request.DefaultPaymentMethod, out var defaultPaymnetMethodId);
            ToModel(request, store, defaultPaymnetMethodId);
            await _storeRepository.UpdateStore(store);
            return Ok(FromModel(store));
        }

        private Client.Models.StoreData FromModel(Data.StoreData data)
        {
            var storeBlob = data.GetStoreBlob();
            return new Client.Models.StoreData()
            {
                Id = data.Id,
                Name = data.StoreName,
                Website = data.StoreWebsite,
                SpeedPolicy = data.SpeedPolicy,
                DefaultPaymentMethod = data.GetDefaultPaymentId(_btcPayNetworkProvider)?.ToStringNormalized(),
                //blob
                //we do not include DefaultCurrencyPairs,Spread, PreferredExchange, RateScripting, RateScript  in this model and instead opt to set it in stores/storeid/rates endpoints
                //we do not include CoinSwitchSettings in this model and instead opt to set it in stores/storeid/coinswitch endpoints
                //we do not include ExcludedPaymentMethods in this model and instead opt to set it in stores/storeid/payment-methods endpoints
                //we do not include EmailSettings in this model and instead opt to set it in stores/storeid/email endpoints
                //we do not include PaymentMethodCriteria because moving the CurrencyValueJsonConverter to the Client csproj is hard and requires a refactor (#1571 & #1572)
                NetworkFeeMode = storeBlob.NetworkFeeMode,
                RequiresRefundEmail = storeBlob.RequiresRefundEmail,
                LightningAmountInSatoshi = storeBlob.LightningAmountInSatoshi,
                LightningPrivateRouteHints = storeBlob.LightningPrivateRouteHints,
                OnChainWithLnInvoiceFallback = storeBlob.OnChainWithLnInvoiceFallback,
                RedirectAutomatically = storeBlob.RedirectAutomatically,
                LazyPaymentMethods = storeBlob.LazyPaymentMethods,
                ShowRecommendedFee = storeBlob.ShowRecommendedFee,
                RecommendedFeeBlockTarget = storeBlob.RecommendedFeeBlockTarget,
                DefaultLang = storeBlob.DefaultLang,
                MonitoringExpiration = storeBlob.MonitoringExpiration,
                InvoiceExpiration = storeBlob.InvoiceExpiration,
                CustomLogo = storeBlob.CustomLogo,
                CustomCSS = storeBlob.CustomCSS,
                HtmlTitle = storeBlob.HtmlTitle,
                AnyoneCanCreateInvoice = storeBlob.AnyoneCanInvoice,
                LightningDescriptionTemplate = storeBlob.LightningDescriptionTemplate,
                PaymentTolerance = storeBlob.PaymentTolerance,
                PayJoinEnabled = storeBlob.PayJoinEnabled
            };
        }

        private static void ToModel(StoreBaseData restModel, Data.StoreData model, PaymentMethodId defaultPaymentMethod)
        {
            var blob = model.GetStoreBlob();

            model.StoreName = restModel.Name;
            model.StoreName = restModel.Name;
            model.StoreWebsite = restModel.Website;
            model.SpeedPolicy = restModel.SpeedPolicy;
            model.SetDefaultPaymentId(defaultPaymentMethod);
            //we do not include the default payment method in this model and instead opt to set it in the stores/storeid/payment-methods endpoints
            //blob
            //we do not include DefaultCurrencyPairs;Spread; PreferredExchange; RateScripting; RateScript  in this model and instead opt to set it in stores/storeid/rates endpoints
            //we do not include CoinSwitchSettings in this model and instead opt to set it in stores/storeid/coinswitch endpoints
            //we do not include ExcludedPaymentMethods in this model and instead opt to set it in stores/storeid/payment-methods endpoints
            //we do not include EmailSettings in this model and instead opt to set it in stores/storeid/email endpoints
            //we do not include OnChainMinValue and LightningMaxValue because moving the CurrencyValueJsonConverter to the Client csproj is hard and requires a refactor (#1571 & #1572)
            blob.NetworkFeeMode = restModel.NetworkFeeMode;
            blob.RequiresRefundEmail = restModel.RequiresRefundEmail;
            blob.LightningAmountInSatoshi = restModel.LightningAmountInSatoshi;
            blob.LightningPrivateRouteHints = restModel.LightningPrivateRouteHints;
            blob.OnChainWithLnInvoiceFallback = restModel.OnChainWithLnInvoiceFallback;
            blob.LazyPaymentMethods = restModel.LazyPaymentMethods;
            blob.RedirectAutomatically = restModel.RedirectAutomatically;
            blob.ShowRecommendedFee = restModel.ShowRecommendedFee;
            blob.RecommendedFeeBlockTarget = restModel.RecommendedFeeBlockTarget;
            blob.DefaultLang = restModel.DefaultLang;
            blob.MonitoringExpiration = restModel.MonitoringExpiration;
            blob.InvoiceExpiration = restModel.InvoiceExpiration;
            blob.CustomLogo = restModel.CustomLogo;
            blob.CustomCSS = restModel.CustomCSS;
            blob.HtmlTitle = restModel.HtmlTitle;
            blob.AnyoneCanInvoice = restModel.AnyoneCanCreateInvoice;
            blob.LightningDescriptionTemplate = restModel.LightningDescriptionTemplate;
            blob.PaymentTolerance = restModel.PaymentTolerance;
            blob.PayJoinEnabled = restModel.PayJoinEnabled;
            model.SetStoreBlob(blob);
        }

        private IActionResult Validate(StoreBaseData request)
        {
            if (request is null)
            {
                return BadRequest();
            }

            if (!string.IsNullOrEmpty(request.DefaultPaymentMethod) &&
                !PaymentMethodId.TryParse(request.DefaultPaymentMethod, out var defaultPaymnetMethodId))
            {
                ModelState.AddModelError(nameof(request.Name), "DefaultPaymentMethod is invalid");
            }
            
            if (string.IsNullOrEmpty(request.Name))
                ModelState.AddModelError(nameof(request.Name), "Name is missing");
            else if (request.Name.Length < 1 || request.Name.Length > 50)
                ModelState.AddModelError(nameof(request.Name), "Name can only be between 1 and 50 characters");
            if (!string.IsNullOrEmpty(request.Website) && !Uri.TryCreate(request.Website, UriKind.Absolute, out _))
            {
                ModelState.AddModelError(nameof(request.Website), "Website is not a valid url");
            }
            if (request.InvoiceExpiration < TimeSpan.FromMinutes(1) && request.InvoiceExpiration > TimeSpan.FromMinutes(60 * 24 * 24))
                ModelState.AddModelError(nameof(request.InvoiceExpiration), "InvoiceExpiration can only be between 1 and 34560 mins");
            if (request.MonitoringExpiration < TimeSpan.FromMinutes(10) && request.MonitoringExpiration > TimeSpan.FromMinutes(60 * 24 * 24))
                ModelState.AddModelError(nameof(request.MonitoringExpiration), "InvoiceExpiration can only be between 10 and 34560 mins");
            if (request.PaymentTolerance < 0 && request.PaymentTolerance > 100)
                ModelState.AddModelError(nameof(request.PaymentTolerance), "PaymentTolerance can only be between 0 and 100 percent");

            return !ModelState.IsValid ? this.CreateValidationError(ModelState) : null;
        }
    }
}
