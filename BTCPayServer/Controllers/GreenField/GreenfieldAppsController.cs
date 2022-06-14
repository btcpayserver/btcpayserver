#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Apps;
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

        public GreenfieldAppsController(
            AppService appService,
            StoreRepository storeRepository,
            UserManager<ApplicationUser> userManager,
            BTCPayNetworkProvider btcPayNetworkProvider
        )
        {
            _appService = appService;
            _storeRepository = storeRepository;
        }

        [HttpPost("~/api/v1/stores/{storeId}/apps/pos")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreatePointOfSaleApp(string storeId, CreatePointOfSaleAppRequest request)
        {
            var validationResult = Validate(request);
            if (validationResult != null)
            {
                return validationResult;
            }
            
            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
                return this.CreateAPIError(404, "store-not-found", "The store was not found");
            
            var defaultCurrency = store.GetStoreBlob().DefaultCurrency;
            var appData = new AppData
            {
                StoreDataId = storeId,
                Name = request.AppName,
                AppType = AppType.PointOfSale.ToString()
            };

            appData.SetSettings(new PointOfSaleSettings
            {
                Title = request.Title,
                DefaultView = (Services.Apps.PosViewType)request.DefaultView,
                ShowCustomAmount = request.ShowCustomAmount,
                ShowDiscount = request.ShowDiscount,
                EnableTips = request.EnableTips,
                Currency = request.Currency ?? defaultCurrency,
                Template = request.Template,
                ButtonText = request.FixedAmountPayButtonText ?? PointOfSaleSettings.BUTTON_TEXT_DEF,
                CustomButtonText = request.CustomAmountPayButtonText ?? PointOfSaleSettings.CUSTOM_BUTTON_TEXT_DEF,
                CustomTipText = request.TipText ?? PointOfSaleSettings.CUSTOM_TIP_TEXT_DEF,
                CustomCSSLink = request.CustomCSSLink,
                NotificationUrl = request.NotificationUrl,
                RedirectUrl = request.RedirectUrl,
                Description = request.Description,
                EmbeddedCSS = request.EmbeddedCSS,
                RedirectAutomatically = request.RedirectAutomatically,
                RequiresRefundEmail = request.RequiresRefundEmail == true ? 
                    RequiresRefundEmail.On : 
                    request.RequiresRefundEmail == false ? 
                        RequiresRefundEmail.Off : 
                        RequiresRefundEmail.InheritFromStore,
            });

            await _appService.UpdateOrCreateApp(appData);

            return Ok(ToModel(appData));
        }

        private PointOfSaleAppData ToModel(AppData appData)
        {
            return new PointOfSaleAppData
            {
                Id = appData.Id,
                AppType = appData.AppType,
                Name = appData.Name,
                StoreId = appData.StoreDataId,
                Created = appData.Created
            };
        }

        private IActionResult? Validate(CreateAppRequest request)
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
