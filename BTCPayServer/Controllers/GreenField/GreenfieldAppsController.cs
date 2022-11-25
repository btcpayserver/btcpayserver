#nullable enable
using System;
using System.Linq;
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

        [HttpPost("~/api/v1/stores/{storeId}/apps/crowdfund")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreateCrowdfundApp(string storeId, CreateCrowdfundAppRequest request)
        {
            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
                return this.CreateAPIError(404, "store-not-found", "The store was not found");

            // This is not obvious but we must have a non-null currency or else request validation may work incorrectly
            request.TargetCurrency = request.TargetCurrency ?? store.GetStoreBlob().DefaultCurrency;

            var validationResult = ValidateCrowdfundAppRequest(request);
            if (validationResult != null)
            {
                return validationResult;
            }

            var appData = new AppData
            {
                StoreDataId = storeId,
                Name = request.AppName,
                AppType = AppType.Crowdfund.ToString()
            };

            appData.SetSettings(ToCrowdfundSettings(request));

            await _appService.UpdateOrCreateApp(appData);

            return Ok(ToCrowdfundModel(appData));
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

            return Ok(ToPointOfSaleModel(appData));
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

            return Ok(ToPointOfSaleModel(app));
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
            var app = await _appService.GetApp(appId, null);
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

        private CrowdfundSettings ToCrowdfundSettings(CreateCrowdfundAppRequest request)
        {
            var parsedSounds = ValidateStringArray(request.Sounds);
            var parsedColors = ValidateStringArray(request.AnimationColors);

            return new CrowdfundSettings
            {
                Title = request.Title?.Trim(),
                Enabled = request.Enabled ?? true,
                EnforceTargetAmount = request.EnforceTargetAmount ?? false,
                StartDate = request.StartDate?.UtcDateTime,
                TargetCurrency = request.TargetCurrency?.Trim(),
                Description = request.Description?.Trim(),
                EndDate = request.EndDate?.UtcDateTime,
                TargetAmount = request.TargetAmount,
                CustomCSSLink = request.CustomCSSLink?.Trim(),
                MainImageUrl = request.MainImageUrl?.Trim(),
                EmbeddedCSS = request.EmbeddedCSS?.Trim(),
                NotificationUrl = request.NotificationUrl?.Trim(),
                Tagline = request.Tagline?.Trim(),
                PerksTemplate = request.PerksTemplate != null ? _appService.SerializeTemplate(_appService.Parse(request.PerksTemplate?.Trim(), request.TargetCurrency)) : null,
                // If Disqus shortname is not null or empty we assume that Disqus should be enabled
                DisqusEnabled = !string.IsNullOrEmpty(request.DisqusShortname?.Trim()),
                DisqusShortname = request.DisqusShortname?.Trim(),
                // If explicit parameter is not passed for enabling sounds/animations, turn them on if custom sounds/colors are passed
                SoundsEnabled = request.SoundsEnabled ?? parsedSounds != null,
                AnimationsEnabled = request.AnimationsEnabled ?? parsedColors != null,
                ResetEveryAmount = request.ResetEveryAmount ?? 1,
                ResetEvery = (Services.Apps.CrowdfundResetEvery)request.ResetEvery,
                DisplayPerksValue = request.DisplayPerksValue ?? false,
                DisplayPerksRanking = request.DisplayPerksRanking ?? false,
                SortPerksByPopularity = request.SortPerksByPopularity ?? false,
                Sounds = parsedSounds ?? new CrowdfundSettings().Sounds,
                AnimationColors = parsedColors ?? new CrowdfundSettings().AnimationColors
            };
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
                FormId = request.FormId,
                CheckoutType = request.CheckoutType ?? CheckoutType.V1
            };
        }

        private AppDataBase ToModel(AppData appData)
        {
            return new AppDataBase
            {
                Id = appData.Id,
                AppType = appData.AppType,
                Name = appData.Name,
                StoreId = appData.StoreDataId,
                Created = appData.Created,
            };
        }

        private PointOfSaleAppData ToPointOfSaleModel(AppData appData)
        {
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

        private CrowdfundAppData ToCrowdfundModel(AppData appData)
        {
            return new CrowdfundAppData
            {
                Id = appData.Id,
                AppType = appData.AppType,
                Name = appData.Name,
                StoreId = appData.StoreDataId,
                Created = appData.Created
            };
        }

        private string[]? ValidateStringArray(string[]? arr)
        {
            if (arr == null || !arr.Any()) 
            {
                return null;
            }

            // Make sure it's not just an array of empty strings
            if (arr.All(s => string.IsNullOrEmpty(s.Trim())))
            {
                return null;
            }

            return arr.Select(s => s.Trim()).ToArray();
        }

        private IActionResult? ValidateCrowdfundAppRequest(CreateCrowdfundAppRequest request)
        {
            var validationResult = ValidateCreateAppRequest(request);
            if (request.TargetCurrency != null && _currencies.GetCurrencyData(request.TargetCurrency, false) == null)
            {
                ModelState.AddModelError(nameof(request.TargetCurrency), "Invalid currency");
            }

            try
            {
                _appService.SerializeTemplate(_appService.Parse(request.PerksTemplate, request.TargetCurrency));
            }
            catch
            {
                ModelState.AddModelError(nameof(request.PerksTemplate), "Invalid template");
            }

            if (request.ResetEvery != Client.Models.CrowdfundResetEvery.Never && request.StartDate == null)
            {
                ModelState.AddModelError(nameof(request.StartDate), "A start date is needed when the goal resets every X amount of time");
            }

            if (request.ResetEvery != Client.Models.CrowdfundResetEvery.Never && request.ResetEveryAmount <= 0)
            {
                ModelState.AddModelError(nameof(request.ResetEveryAmount), "You must reset the goal at a minimum of 1");
            }

            if (request.Sounds != null && ValidateStringArray(request.Sounds) == null)
            {
                ModelState.AddModelError(nameof(request.Sounds), "Sounds must be a non-empty array of non-empty strings");
            }

            if (request.AnimationColors != null && ValidateStringArray(request.AnimationColors) == null)
            {
                ModelState.AddModelError(nameof(request.AnimationColors), "Animation colors must be a non-empty array of non-empty strings");
            }

            if (request.StartDate != null && request.EndDate != null && DateTimeOffset.Compare((DateTimeOffset)request.StartDate, (DateTimeOffset)request.EndDate!) > 0)
            {
                ModelState.AddModelError(nameof(request.EndDate), "End date cannot be before start date");
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
