#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Abstractions.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldAppsController : ControllerBase
    {
        private readonly AppService _appService;
        private readonly StoreRepository _storeRepository;
        private readonly CurrencyNameTable _currencies;

        public GreenfieldAppsController(
            AppService appService,
            StoreRepository storeRepository,
            UserManager<ApplicationUser> userManager,
            BTCPayNetworkProvider btcPayNetworkProvider,
            CurrencyNameTable currencies
        )
        {
            _appService = appService;
            _storeRepository = storeRepository;
            _currencies = currencies;
        }

        [HttpPost("~/api/v1/stores/{storeId}/apps/pos")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreatePointOfSaleApp(string storeId, CreatePointOfSaleAppRequest request)
        {
            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
                return this.CreateAPIError(404, "store-not-found", "The store was not found");

            // This is not obvious but we must have a non-null currency or else request validation may work incorrectly
            request.Currency = request.Currency ?? store.GetStoreBlob().DefaultCurrency;

            var validationResult = ValidatePOSAppRequest(request);
            if (validationResult != null)
            {
                return validationResult;
            }
            
            var appData = new AppData
            {
                StoreDataId = storeId,
                Name = request.AppName,
                AppType = AppType.PointOfSale.ToString()
            };

            appData.SetSettings(ToPointOfSaleSettings(request));

            await _appService.UpdateOrCreateApp(appData);

            return Ok(ToModel(appData));
        }

        [HttpPut("~/api/v1/apps/pos/{appId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> UpdatePointOfSaleApp(string appId, CreatePointOfSaleAppRequest request)
        {
            var app = await _appService.GetApp(appId, AppType.PointOfSale);
            if (app == null)
            {
                return AppNotFound();
            }

            var settings = app.GetSettings<PointOfSaleSettings>();
            
            // This is not obvious but we must have a non-null currency or else request validation may work incorrectly
            request.Currency = request.Currency ?? settings.Currency;

            var validationResult = ValidatePOSAppRequest(request);
            if (validationResult != null)
            {
                return validationResult;
            }

            app.Name = request.AppName;
            app.SetSettings(ToPointOfSaleSettings(request));
            
            await _appService.UpdateOrCreateApp(app);

            return Ok(ToModel(app));
        }

        private RequiresRefundEmail? BoolToRequiresRefundEmail(bool? requiresRefundEmail)
        {
            switch (requiresRefundEmail)
            {
                case true:
                    return RequiresRefundEmail.On;
                case false:
                    return RequiresRefundEmail.Off;
                default:
                    return null;
            }
        }

        [HttpGet("~/api/v1/apps/{appId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetApp(string appId)
        {
            var app = await _appService.GetApp(appId, AppType.PointOfSale);
            if (app == null)
            {
                return AppNotFound();
            }
                
            return Ok(ToModel(app));
        }

        [HttpDelete("~/api/v1/apps/{appId}")]
        public async Task<IActionResult> DeleteApp(string appId)
        {
            var app = await _appService.GetApp(appId, null);
            if (app == null)
            {
                return AppNotFound();
            }
                
            await _appService.DeleteApp(app);

            return Ok();
        }

        private IActionResult AppNotFound()
        {
            return this.CreateAPIError(404, "app-not-found", "The app with specified ID was not found");
        }

        private PointOfSaleSettings ToPointOfSaleSettings(CreatePointOfSaleAppRequest request)
        {
            return new PointOfSaleSettings()
            {
                Title = request.Title,
                DefaultView = (Services.Apps.PosViewType)request.DefaultView,
                ShowCustomAmount = request.ShowCustomAmount,
                ShowDiscount = request.ShowDiscount,
                EnableTips = request.EnableTips,
                Currency = request.Currency,
                Template = request.Template != null ? _appService.SerializeTemplate(_appService.Parse(request.Template, request.Currency)) : null,
                ButtonText = request.FixedAmountPayButtonText ?? PointOfSaleSettings.BUTTON_TEXT_DEF,
                CustomButtonText = request.CustomAmountPayButtonText ?? PointOfSaleSettings.CUSTOM_BUTTON_TEXT_DEF,
                CustomTipText = request.TipText ?? PointOfSaleSettings.CUSTOM_TIP_TEXT_DEF,
                CustomCSSLink = request.CustomCSSLink,
                NotificationUrl = request.NotificationUrl,
                RedirectUrl = request.RedirectUrl,
                Description = request.Description,
                EmbeddedCSS = request.EmbeddedCSS,
                RedirectAutomatically = request.RedirectAutomatically,
                RequiresRefundEmail = BoolToRequiresRefundEmail(request.RequiresRefundEmail) ?? RequiresRefundEmail.InheritFromStore,
            };
        }

        private PointOfSaleAppData ToModel(AppData appData)
        {
            var settings = appData.GetSettings<PointOfSaleSettings>();

            return new PointOfSaleAppData
            {
                Id = appData.Id,
                AppType = appData.AppType,
                Name = appData.Name,
                StoreId = appData.StoreDataId,
                Created = appData.Created,
            };
        }

        private IActionResult? ValidatePOSAppRequest(CreatePointOfSaleAppRequest request)
        {
            var validationResult = ValidateCreateAppRequest(request);
            if (request.Currency != null && _currencies.GetCurrencyData(request.Currency, false) == null)
            {
                ModelState.AddModelError(nameof(request.Currency), "Invalid currency");
            }

            if (request.Template != null)
            {
                try
                {
                    _appService.SerializeTemplate(_appService.Parse(request.Template, request.Currency));
                }
                catch
                {
                    ModelState.AddModelError(nameof(request.Template), "Invalid template");
                }
            }
            
            if (!ModelState.IsValid)
            {
                validationResult = this.CreateValidationError(ModelState);
            }

            return validationResult;
        }

        private IActionResult? ValidateCreateAppRequest(CreateAppRequest request)
        {
            if (request is null)
            {
                return BadRequest();
            }

            if (string.IsNullOrEmpty(request.AppName))
            {
                ModelState.AddModelError(nameof(request.AppName), "App name is missing");
            }
            else if (request.AppName.Length < 1 || request.AppName.Length > 50)
            {
                ModelState.AddModelError(nameof(request.AppName), "Name can only be between 1 and 50 characters");
            }

            return !ModelState.IsValid ? this.CreateValidationError(ModelState) : null;
        }
    }
}
