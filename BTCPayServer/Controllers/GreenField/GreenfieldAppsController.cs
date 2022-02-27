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

        [HttpPost("~/api/v1/apps")]
        [Authorize(Policy = Policies.CanModifyStoreSettingsUnscoped, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateApp(CreateAppRequest request)
        {
            var validationResult = await Validate(request);
            if (validationResult != null)
            {
                return validationResult;
            }

            var appData = new AppData
            {
                StoreDataId = request.StoreId,
                Name = request.AppName,
                AppType = request.AppType
            };

            var defaultCurrency = (await _storeRepository.FindStore(request.StoreId)).GetStoreBlob().DefaultCurrency;
            Enum.TryParse(request.AppType, out AppType appType);
            switch (appType)
            {
                case AppType.Crowdfund:
                    var emptyCrowdfund = new CrowdfundSettings { TargetCurrency = defaultCurrency };
                    appData.SetSettings(emptyCrowdfund);
                    break;
                case AppType.PointOfSale:
                    var empty = new PointOfSaleSettings { Currency = defaultCurrency };
                    appData.SetSettings(empty);
                    break;
            }

            await _appService.UpdateOrCreateApp(appData);

            return Ok(appData);
        }

        async private Task<IActionResult?> Validate(CreateAppRequest request)
        {
            if (request is null)
            {
                return BadRequest();
            }

            if (!Enum.TryParse(request.AppType, out AppType appType))
            {
                ModelState.AddModelError(nameof(request.AppType), "Invalid app type");
            }

            if (string.IsNullOrEmpty(request.AppName))
            {
                ModelState.AddModelError(nameof(request.AppName), "App name is missing");
            }
            else if (request.AppName.Length < 1 || request.AppName.Length > 50)
            {
                ModelState.AddModelError(nameof(request.AppName), "Name can only be between 1 and 50 characters");
            }

            var store = await _storeRepository.FindStore(request.StoreId);
            if (store == null)
            {
                ModelState.AddModelError(nameof(request.StoreId), "Store with provided ID not found");
            }           

            return !ModelState.IsValid ? this.CreateValidationError(ModelState) : null;
        }
    }
}
