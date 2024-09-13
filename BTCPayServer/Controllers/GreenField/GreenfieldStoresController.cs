using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoresController : ControllerBase
    {
        private readonly StoreRepository _storeRepository;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IFileService _fileService;
        private readonly UriResolver _uriResolver;

        public GreenfieldStoresController(
            StoreRepository storeRepository,
            UserManager<ApplicationUser> userManager,
            IFileService fileService,
            UriResolver uriResolver)
        {
            _storeRepository = storeRepository;
            _userManager = userManager;
            _fileService = fileService;
            _uriResolver = uriResolver;
        }
        
        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores")]
        public async Task<ActionResult<IEnumerable<Client.Models.StoreData>>> GetStores()
        {
            var storesData = HttpContext.GetStoresData();
            var stores = new List<Client.Models.StoreData>();
            foreach (var storeData in storesData)
            {
                stores.Add(await FromModel(storeData));
            }
            return Ok(stores);
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}")]
        public async Task<IActionResult> GetStore(string storeId)
        {
            var store = HttpContext.GetStoreData();
            return store == null ? StoreNotFound() : Ok(await FromModel(store));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}")]
        public async Task<IActionResult> RemoveStore(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null) return StoreNotFound();

            await _storeRepository.RemoveStore(storeId, _userManager.GetUserId(User));
            return Ok();
        }

        [HttpPost("~/api/v1/stores")]
        [Authorize(Policy = Policies.CanModifyStoreSettingsUnscoped, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateStore(CreateStoreRequest request)
        {
            var validationResult = Validate(request);
            if (validationResult != null) return validationResult;

            var store = new StoreData();
            PaymentMethodId.TryParse(request.DefaultPaymentMethod, out var defaultPaymentMethodId);
            ToModel(request, store, defaultPaymentMethodId);
            await _storeRepository.CreateStore(_userManager.GetUserId(User), store);
            return Ok(await FromModel(store));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}")]
        public async Task<IActionResult> UpdateStore(string storeId, UpdateStoreRequest request)
        {
            var store = HttpContext.GetStoreData();
            if (store == null) return StoreNotFound();
            var validationResult = Validate(request);
            if (validationResult != null) return validationResult;

            PaymentMethodId.TryParse(request.DefaultPaymentMethod, out var defaultPaymentMethodId);
            ToModel(request, store, defaultPaymentMethodId);
            await _storeRepository.UpdateStore(store);
            return Ok(await FromModel(store));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/logo")]
        public async Task<IActionResult> UploadStoreLogo(string storeId, IFormFile file)
        {
            var store = HttpContext.GetStoreData();
            if (store == null) return StoreNotFound();

            if (file is null)
                ModelState.AddModelError(nameof(file), "Invalid file");
            else if (file.Length > 1_000_000)
                ModelState.AddModelError(nameof(file), "The uploaded image file should be less than 1MB");
            else if (!file.ContentType.StartsWith("image/", StringComparison.InvariantCulture))
                ModelState.AddModelError(nameof(file), "The uploaded file needs to be an image");
            else if (!file.FileName.IsValidFileName())
                ModelState.AddModelError(nameof(file.FileName), "Invalid filename");
            else
            {
                var formFile = await file.Bufferize();
                if (!FileTypeDetector.IsPicture(formFile.Buffer, formFile.FileName))
                    ModelState.AddModelError(nameof(file), "The uploaded file needs to be an image");
            }
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            try
            {
                var userId = _userManager.GetUserId(User)!;
                var storedFile = await _fileService.AddFile(file!, userId);
                var blob = store.GetStoreBlob();
                blob.LogoUrl = new UnresolvedUri.FileIdUri(storedFile.Id);
                store.SetStoreBlob(blob);
                await _storeRepository.UpdateStore(store);

                return Ok(await FromModel(store));
            }
            catch (Exception e)
            {
                return this.CreateAPIError(404, "file-upload-failed", e.Message);
            }
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/logo")]
        public async Task<IActionResult> DeleteStoreLogo(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null) return StoreNotFound();
            
            var blob = store.GetStoreBlob();
            var fileId = (blob.LogoUrl as UnresolvedUri.FileIdUri)?.FileId;
            if (!string.IsNullOrEmpty(fileId))
            {
                var userId = _userManager.GetUserId(User)!;
                await _fileService.RemoveFile(fileId, userId);
                blob.LogoUrl = null;
                store.SetStoreBlob(blob);
                await _storeRepository.UpdateStore(store);
            }
            return Ok();
        }

        internal async Task<Client.Models.StoreData> FromModel(StoreData data)
        {
            var storeBlob = data.GetStoreBlob();
            return new Client.Models.StoreData
            {
                Id = data.Id,
                Name = data.StoreName,
                Website = data.StoreWebsite,
                Archived = data.Archived,
                BrandColor = storeBlob.BrandColor,
                ApplyBrandColorToBackend = storeBlob.ApplyBrandColorToBackend,
                CssUrl = storeBlob.CssUrl == null ? null : await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), storeBlob.CssUrl),
                LogoUrl = storeBlob.LogoUrl == null ? null : await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), storeBlob.LogoUrl),
                PaymentSoundUrl = storeBlob.PaymentSoundUrl == null ? null : await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), storeBlob.PaymentSoundUrl),
                SupportUrl = storeBlob.StoreSupportUrl,
                SpeedPolicy = data.SpeedPolicy,
                DefaultPaymentMethod = data.GetDefaultPaymentId()?.ToString(),
                //blob
                //we do not include DefaultCurrencyPairs,Spread, PreferredExchange, RateScripting, RateScript  in this model and instead opt to set it in stores/storeid/rates endpoints
                //we do not include ExcludedPaymentMethods in this model and instead opt to set it in stores/storeid/payment-methods endpoints
                //we do not include EmailSettings in this model and instead opt to set it in stores/storeid/email endpoints
                //we do not include PaymentMethodCriteria because moving the CurrencyValueJsonConverter to the Client csproj is hard and requires a refactor (#1571 & #1572)
                NetworkFeeMode = storeBlob.NetworkFeeMode,
                DefaultCurrency = storeBlob.DefaultCurrency,
                Receipt = InvoiceDataBase.ReceiptOptions.Merge(storeBlob.ReceiptOptions, null),
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
                DisplayExpirationTimer = storeBlob.DisplayExpirationTimer,
                HtmlTitle = storeBlob.HtmlTitle,
                AnyoneCanCreateInvoice = storeBlob.AnyoneCanInvoice,
                LightningDescriptionTemplate = storeBlob.LightningDescriptionTemplate,
                PaymentTolerance = storeBlob.PaymentTolerance,
                PayJoinEnabled = storeBlob.PayJoinEnabled,
                AutoDetectLanguage = storeBlob.AutoDetectLanguage,
                ShowPayInWalletButton = storeBlob.ShowPayInWalletButton,
                ShowStoreHeader = storeBlob.ShowStoreHeader,
                CelebratePayment = storeBlob.CelebratePayment,
                PlaySoundOnPayment = storeBlob.PlaySoundOnPayment,
                PaymentMethodCriteria = storeBlob.PaymentMethodCriteria?.Where(criteria => criteria.Value is not null).Select(criteria => new PaymentMethodCriteriaData
                {
                    Above = criteria.Above,
                    Amount = criteria.Value.Value,
                    CurrencyCode = criteria.Value.Currency,
                    PaymentMethodId = criteria.PaymentMethod.ToString()
                }).ToList() ?? new List<PaymentMethodCriteriaData>()
            };
        }

        private void ToModel(StoreBaseData restModel, StoreData model, PaymentMethodId defaultPaymentMethod)
        {
            var blob = model.GetStoreBlob();
            model.StoreName = restModel.Name;
            model.StoreWebsite = restModel.Website;
            model.Archived = restModel.Archived;
            model.SpeedPolicy = restModel.SpeedPolicy;
            model.SetDefaultPaymentId(defaultPaymentMethod);
            //we do not include the default payment method in this model and instead opt to set it in the stores/storeid/payment-methods endpoints
            //blob
            //we do not include DefaultCurrencyPairs;Spread; PreferredExchange; RateScripting; RateScript  in this model and instead opt to set it in stores/storeid/rates endpoints
            //we do not include ExcludedPaymentMethods in this model and instead opt to set it in stores/storeid/payment-methods endpoints
            //we do not include EmailSettings in this model and instead opt to set it in stores/storeid/email endpoints
            //we do not include OnChainMinValue and LightningMaxValue because moving the CurrencyValueJsonConverter to the Client csproj is hard and requires a refactor (#1571 & #1572)
            blob.NetworkFeeMode = restModel.NetworkFeeMode;
            blob.DefaultCurrency = restModel.DefaultCurrency;
            blob.ReceiptOptions = InvoiceDataBase.ReceiptOptions.Merge(restModel.Receipt, null);
            blob.LightningAmountInSatoshi = restModel.LightningAmountInSatoshi;
            blob.LightningPrivateRouteHints = restModel.LightningPrivateRouteHints;
            blob.OnChainWithLnInvoiceFallback = restModel.OnChainWithLnInvoiceFallback;
            blob.LazyPaymentMethods = restModel.LazyPaymentMethods;
            blob.RedirectAutomatically = restModel.RedirectAutomatically;
            blob.ShowRecommendedFee = restModel.ShowRecommendedFee;
            blob.RecommendedFeeBlockTarget = restModel.RecommendedFeeBlockTarget;
            blob.DefaultLang = restModel.DefaultLang;
            blob.StoreSupportUrl = restModel.SupportUrl;
            blob.MonitoringExpiration = restModel.MonitoringExpiration;
            blob.InvoiceExpiration = restModel.InvoiceExpiration;
            blob.DisplayExpirationTimer = restModel.DisplayExpirationTimer;
            blob.HtmlTitle = restModel.HtmlTitle;
            blob.AnyoneCanInvoice = restModel.AnyoneCanCreateInvoice;
            blob.LightningDescriptionTemplate = restModel.LightningDescriptionTemplate;
            blob.PaymentTolerance = restModel.PaymentTolerance;
            blob.PayJoinEnabled = restModel.PayJoinEnabled;
            blob.BrandColor = restModel.BrandColor;
            blob.ApplyBrandColorToBackend = restModel.ApplyBrandColorToBackend;
            blob.LogoUrl = restModel.LogoUrl is null ? null : UnresolvedUri.Create(restModel.LogoUrl);
            blob.CssUrl = restModel.CssUrl is null ? null : UnresolvedUri.Create(restModel.CssUrl);
            blob.PaymentSoundUrl = restModel.PaymentSoundUrl is null ? null : UnresolvedUri.Create(restModel.PaymentSoundUrl);
            if (restModel.AutoDetectLanguage.HasValue)
                blob.AutoDetectLanguage = restModel.AutoDetectLanguage.Value;
            if (restModel.ShowPayInWalletButton.HasValue)
                blob.ShowPayInWalletButton = restModel.ShowPayInWalletButton.Value;
            if (restModel.ShowStoreHeader.HasValue)
                blob.ShowStoreHeader = restModel.ShowStoreHeader.Value;
            if (restModel.CelebratePayment.HasValue)
                blob.CelebratePayment = restModel.CelebratePayment.Value;
            if (restModel.PlaySoundOnPayment.HasValue)
                blob.PlaySoundOnPayment = restModel.PlaySoundOnPayment.Value;
            blob.PaymentMethodCriteria = restModel.PaymentMethodCriteria?.Select(criteria =>
                new PaymentMethodCriteria
                {
                    Above = criteria.Above,
                    Value = new CurrencyValue
                    {
                        Currency = criteria.CurrencyCode,
                        Value = criteria.Amount
                    },
                    PaymentMethod = PaymentMethodId.Parse(criteria.PaymentMethodId)
                }).ToList() ?? new List<PaymentMethodCriteria>();
            model.SetStoreBlob(blob);
        }

        private IActionResult Validate(StoreBaseData request)
        {
            if (request is null)
            {
                return BadRequest();
            }

            if (!string.IsNullOrEmpty(request.DefaultPaymentMethod) &&
                !PaymentMethodId.TryParse(request.DefaultPaymentMethod, out _))
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
            if (!string.IsNullOrEmpty(request.LogoUrl) && !Uri.TryCreate(request.LogoUrl, UriKind.Absolute, out _))
            {
                ModelState.AddModelError(nameof(request.LogoUrl), "Logo is not a valid url");
            }
            if (!string.IsNullOrEmpty(request.CssUrl) && !Uri.TryCreate(request.CssUrl, UriKind.Absolute, out _))
            {
                ModelState.AddModelError(nameof(request.CssUrl), "CSS is not a valid url");
            }
            if (!string.IsNullOrEmpty(request.BrandColor) && !ColorPalette.IsValid(request.BrandColor))
            {
                ModelState.AddModelError(nameof(request.BrandColor), "Brand color is not a valid HEX Color (e.g. #F7931A)");
            }
            if (request.InvoiceExpiration < TimeSpan.FromMinutes(1) && request.InvoiceExpiration > TimeSpan.FromMinutes(60 * 24 * 24))
                ModelState.AddModelError(nameof(request.InvoiceExpiration), "InvoiceExpiration can only be between 1 and 34560 mins");
            if (request.DisplayExpirationTimer < TimeSpan.FromMinutes(1) && request.DisplayExpirationTimer > TimeSpan.FromMinutes(60 * 24 * 24))
                ModelState.AddModelError(nameof(request.DisplayExpirationTimer), "DisplayExpirationTimer can only be between 1 and 34560 mins");
            if (request.MonitoringExpiration < TimeSpan.FromMinutes(10) && request.MonitoringExpiration > TimeSpan.FromMinutes(60 * 24 * 24))
                ModelState.AddModelError(nameof(request.MonitoringExpiration), "MonitoringExpiration can only be between 10 and 34560 mins");
            if (request.PaymentTolerance < 0 || request.PaymentTolerance > 100)
                ModelState.AddModelError(nameof(request.PaymentTolerance), "PaymentTolerance can only be between 0 and 100 percent");

            if (request.PaymentMethodCriteria?.Any() is true)
            {
                for (int index = 0; index < request.PaymentMethodCriteria.Count; index++)
                {
                    PaymentMethodCriteriaData pmc = request.PaymentMethodCriteria[index];
                    if (string.IsNullOrEmpty(pmc.CurrencyCode))
                    {
                        request.AddModelError(data => data.PaymentMethodCriteria[index].CurrencyCode, "CurrencyCode is required", this);
                    }
                    else if (CurrencyNameTable.Instance.GetCurrencyData(pmc.CurrencyCode, false) is null)
                    {
                        request.AddModelError(data => data.PaymentMethodCriteria[index].CurrencyCode, "CurrencyCode is invalid", this);
                    }

                    if (string.IsNullOrEmpty(pmc.PaymentMethodId) || PaymentMethodId.TryParse(pmc.PaymentMethodId) is null)
                    {
                        request.AddModelError(data => data.PaymentMethodCriteria[index].PaymentMethodId, "Payment method was invalid", this);
                    }

                    if (pmc.Amount < 0)
                    {
                        request.AddModelError(data => data.PaymentMethodCriteria[index].Amount, "Amount must be greater than 0", this);
                    }
                }
            }
            return !ModelState.IsValid ? this.CreateValidationError(ModelState) : null;
        }

        private IActionResult StoreNotFound()
        {
            return this.CreateAPIError(404, "store-not-found", "The store was not found");
        }
    }
}
