using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Forms;
using BTCPayServer.Forms.Models;
using BTCPayServer.Models;
using BTCPayServer.Plugins.Crowdfund.Models;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitpayClient;
using Newtonsoft.Json.Linq;
using NicolasDorier.RateLimits;
using CrowdfundResetEvery = BTCPayServer.Services.Apps.CrowdfundResetEvery;

namespace BTCPayServer.Plugins.Crowdfund.Controllers
{
    [AutoValidateAntiforgeryToken]
    [Route("apps")]
    public class UICrowdfundController : Controller
    {
        public UICrowdfundController(
            AppService appService,
            CurrencyNameTable currencies,
            EventAggregator eventAggregator,
            UriResolver uriResolver,
            StoreRepository storeRepository,
            IFileService fileService,
            UIInvoiceController invoiceController,
            UserManager<ApplicationUser> userManager,
            FormDataService formDataService,
            CrowdfundAppType app)
        {
            _currencies = currencies;
            _appService = appService;
            _userManager = userManager;
            _app = app;
            _fileService = fileService;
            _storeRepository = storeRepository;
            _eventAggregator = eventAggregator;
            _uriResolver = uriResolver;
            _invoiceController = invoiceController;
            FormDataService = formDataService;
        }

        private readonly EventAggregator _eventAggregator;
        private readonly IFileService _fileService;
        private readonly UriResolver _uriResolver;
        private readonly CurrencyNameTable _currencies;
        private readonly StoreRepository _storeRepository;
        private readonly AppService _appService;
        private readonly UIInvoiceController _invoiceController;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly CrowdfundAppType _app;
        public FormDataService FormDataService { get; }

        [HttpGet("/")]
        [HttpGet("/apps/{appId}/crowdfund")]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        [DomainMappingConstraint(CrowdfundAppType.AppType)]
        public async Task<IActionResult> ViewCrowdfund(string appId)
        {
            var app = await _appService.GetApp(appId, CrowdfundAppType.AppType, true);

            if (app == null)
                return NotFound();
            var settings = app.GetSettings<CrowdfundSettings>();

            var isAdmin = await _appService.GetAppDataIfOwner(GetUserId(), appId, CrowdfundAppType.AppType) != null;

            var hasEnoughSettingsToLoad = !string.IsNullOrEmpty(settings.TargetCurrency);
            if (!hasEnoughSettingsToLoad)
            {
                if (!isAdmin)
                    return NotFound();

                return NotFound("A Target Currency must be set for this app in order to be loadable.");
            }
            var appInfo = await GetAppInfo(appId);

            if (settings.Enabled)
                return View("Crowdfund/Public/ViewCrowdfund", appInfo);
            if (!isAdmin)
                return NotFound();

            return View("Crowdfund/Public/ViewCrowdfund", appInfo);
        }

        [HttpPost("/")]
        [HttpPost("/apps/{appId}/crowdfund")]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        [DomainMappingConstraint(CrowdfundAppType.AppType)]
        [RateLimitsFilter(ZoneLimits.PublicInvoices, Scope = RateLimitsScope.RemoteAddress)]
        public async Task<IActionResult> ContributeToCrowdfund(string appId, ContributeToCrowdfund request, string formResponse = null, CancellationToken cancellationToken = default)
        {
            var app = await _appService.GetApp(appId, CrowdfundAppType.AppType, true);

            if (app == null)
                return NotFound();
            var settings = app.GetSettings<CrowdfundSettings>();

            var isAdmin = await _appService.GetAppDataIfOwner(GetUserId(), appId, CrowdfundAppType.AppType) != null;

            if (!settings.Enabled && !isAdmin)
            {
                return NotFound("Crowdfund is not currently active");
            }

            var info = await GetAppInfo(appId);
            if (!isAdmin &&
                ((settings.StartDate.HasValue && DateTime.UtcNow < settings.StartDate) ||
                 (settings.EndDate.HasValue && DateTime.UtcNow > settings.EndDate) ||
                 (settings.EnforceTargetAmount &&
                  (info.Info.PendingProgressPercentage.GetValueOrDefault(0) +
                   info.Info.ProgressPercentage.GetValueOrDefault(0)) >= 100)))
            {
                return NotFound("Crowdfund is not currently active");
            }

            JObject formResponseJObject = null;
            var store = await _appService.GetStore(app);
            decimal? price = request.Amount;
            var title = settings.Title;
            Dictionary<string, InvoiceSupportedTransactionCurrency> paymentMethods = null;
            ViewPointOfSaleViewModel.Item choice = null;
            if (!string.IsNullOrEmpty(request.ChoiceKey))
            {
                var choices = AppService.Parse(settings.PerksTemplate, false);
                choice = choices?.FirstOrDefault(c => c.Id == request.ChoiceKey);
                if (choice == null)
                    return NotFound("Incorrect option provided");
                title = choice.Title;

                if (choice.PriceType == ViewPointOfSaleViewModel.ItemPriceType.Topup)
                {
                    price = null;
                }
                else
                {
                    price = choice.Price.Value;
                    if (request.Amount > price)
                        price = request.Amount;
                }
                if (choice.Inventory.HasValue)
                {
                    if (choice.Inventory <= 0)
                    {
                        return NotFound("Option was out of stock");
                    }
                }
                if (choice?.PaymentMethods?.Any() is true)
                {
                    paymentMethods = choice?.PaymentMethods.ToDictionary(s => s,
                        s => new InvoiceSupportedTransactionCurrency() { Enabled = true });
                }
            }
            else
            {
                if (request.Amount < 0)
                {
                    return NotFound("Please provide an amount greater than 0");
                }

                price = request.Amount;
            }
            
            if (settings.FormId is not null)
            {
                var formData = await FormDataService.GetForm(settings.FormId);
                if (formData is not null)
                {
                    formResponseJObject = TryParseJObject(formResponse) ?? new JObject();
                    var form = Form.Parse(formData.Config);
                    FormDataService.SetValues(form, formResponseJObject);
                    if (!FormDataService.Validate(form, ModelState))
                    {
                        // someone tried to bypass validation
                        return RedirectToAction(nameof(ViewCrowdfund), new { appId });
                    }
                    
                    
                    var amtField = form.GetFieldByFullName($"{FormDataService.InvoiceParameterPrefix}amount");
                    if (amtField is null)
                    {
                        form.Fields.Add(new Field
                        {
                            Name = $"{FormDataService.InvoiceParameterPrefix}amount",
                            Type = "hidden",
                            Value = price?.ToString(),
                            Constant = true
                        });
                    }
                    else
                    {
                        amtField.Value = price?.ToString();
                    }
                    formResponseJObject = FormDataService.GetValues(form);
                    
                    var invoiceRequest = FormDataService.GenerateInvoiceParametersFromForm(form);
                    if (invoiceRequest.Amount is not null)
                    {
                        price = invoiceRequest.Amount.Value;
                    }
                }
            }

            

            if (!isAdmin && (settings.EnforceTargetAmount && info.TargetAmount.HasValue && price >
                             (info.TargetAmount - (info.Info.CurrentAmount + info.Info.CurrentPendingAmount))))
            {
                return NotFound("Contribution Amount is more than is currently allowed.");
            }

            try
            {
                var appPath = await _appService.ViewLink(app);
                var appUrl = HttpContext.Request.GetAbsoluteUri(appPath);
                var invoice = await _invoiceController.CreateInvoiceCoreRaw(new CreateInvoiceRequest()
                {
                    Amount = price,
                    Currency = settings.TargetCurrency,
                    Metadata = new InvoiceMetadata()
                    {
                        OrderId = AppService.GetRandomOrderId(),
                        ItemCode = request.ChoiceKey ?? string.Empty,
                        ItemDesc = title,
                        BuyerEmail = request.Email
                    }.ToJObject(),
                    Checkout = new InvoiceDataBase.CheckoutOptions()
                    {
                        RedirectURL = request.RedirectUrl ?? appUrl,
                        PaymentMethods = paymentMethods?.Where(p => p.Value.Enabled)
                                                    .Select(p => p.Key).ToArray()
                    },
                    AdditionalSearchTerms = new[] { AppService.GetAppSearchTerm(app) }
                }, store, HttpContext.Request.GetAbsoluteRoot(),
                    new List<string> { AppService.GetAppInternalTag(appId) },
                    cancellationToken, entity =>
                    {
                        entity.NotificationURLTemplate = settings.NotificationUrl;
                        entity.FullNotifications = true;
                        entity.ExtendedNotifications = true;
                        entity.Metadata.OrderUrl = appUrl;
                        if (formResponseJObject is null)
                            return;
                        var meta = entity.Metadata.ToJObject();
                        meta.Merge(formResponseJObject);
                        entity.Metadata = InvoiceMetadata.FromJObject(meta);
                    });

                if (request.RedirectToCheckout)
                {
                    return RedirectToAction(nameof(UIInvoiceController.Checkout), "UIInvoice",
                        new { invoiceId = invoice.Id });
                }

                return Ok(invoice.Id);
            }
            catch (BitpayHttpException e)
            {
                return BadRequest(e.Message);
            }
        }

        private JObject TryParseJObject(string posData)
        {
            try
            {
                return JObject.Parse(posData);
            }
            catch
            {
            }
            return null;
        }

        [HttpGet("/apps/{appId}/crowdfund/form")]
        [IgnoreAntiforgeryToken]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        public async Task<IActionResult> CrowdfundForm(string appId, decimal? amount=0, string choiceKey="")
        {
            var app = await _appService.GetApp(appId, CrowdfundAppType.AppType);
            if (app == null)
                return NotFound();

            var settings = app.GetSettings<CrowdfundSettings>();
            var formData = await FormDataService.GetForm(settings.FormId);
            if (formData is null)
            {
                return RedirectToAction(nameof(ViewCrowdfund), new { appId });
            }

            var prefix = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16)) + "_";
            var formParameters = new MultiValueDictionary<string, string>();
            var controller = nameof(UICrowdfundController).TrimEnd("Controller", StringComparison.InvariantCulture);
            var store = await _appService.GetStore(app);
            var storeBlob = store.GetStoreBlob();
            var form = Form.Parse(formData.Config);
            form.ApplyValuesFromForm(Request.Query);
            var vm = new FormViewModel
            {
                StoreName = store.StoreName,
                StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob),
                FormName = formData.Name,
                Form = form,
                AspController = controller,
                AspAction = nameof(CrowdfundFormSubmit),
                RouteParameters = new Dictionary<string, string> { { "appId", appId }, { "amount", amount.ToString() }, { "choiceKey", choiceKey } },
                FormParameters = formParameters,
                FormParameterPrefix = prefix
            };

            return View("Views/UIForms/View", vm);
        }

        [HttpPost("/apps/{appId}/crowdfund/form/submit")]
        [IgnoreAntiforgeryToken]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        public async Task<IActionResult> CrowdfundFormSubmit(string appId, decimal amount, string choiceKey, FormViewModel viewModel)
        {
            var app = await _appService.GetApp(appId, CrowdfundAppType.AppType);
            if (app == null)
                return NotFound();

            var settings = app.GetSettings<CrowdfundSettings>();
            var formData = await FormDataService.GetForm(settings.FormId);
            if (formData is null)
            {
                return RedirectToAction(nameof(ViewCrowdfund));
            }
            var form = Form.Parse(formData.Config);
            var formFieldNames = form.GetAllFields().Select(tuple => tuple.FullName).Distinct().ToArray();

            // For unit testing
            if (Request.Headers.Count == 1)
            {
                Request.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
            }
            var formParameters = Request.Form
                .Where(pair => pair.Key.StartsWith(viewModel.FormParameterPrefix))
                .ToDictionary(pair => pair.Key.Replace(viewModel.FormParameterPrefix, string.Empty), pair => pair.Value)
                .ToMultiValueDictionary(p => p.Key, p => p.Value.ToString());

            form.ApplyValuesFromForm(Request.Form.Where(pair => formFieldNames.Contains(pair.Key)));

            if (FormDataService.Validate(form, ModelState))
            {
                var appInfo = await GetAppInfo(appId);
                var req = new ContributeToCrowdfund()
                {
                    RedirectToCheckout = true,
                    Amount = amount == 0 ? null : amount,
                    ChoiceKey = choiceKey,
                    ViewCrowdfundViewModel = appInfo
                };

                return ContributeToCrowdfund(appId, req, formResponse: FormDataService.GetValues(form).ToString()).Result;
            }

            viewModel.FormName = formData.Name;
            viewModel.Form = form;

            viewModel.FormParameters = formParameters;
            return View("Views/UIForms/View", viewModel);
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("{appId}/settings/crowdfund")]
        public async Task<IActionResult> UpdateCrowdfund(string appId)
        {
            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            var settings = app.GetSettings<CrowdfundSettings>();
            var resetEvery = Enum.GetName(typeof(CrowdfundResetEvery), settings.ResetEvery);

            var vm = new UpdateCrowdfundViewModel
            {
                Title = settings.Title,
                StoreId = app.StoreDataId,
                StoreName = app.StoreData?.StoreName,
                StoreDefaultCurrency = await GetStoreDefaultCurrentIfEmpty(app.StoreDataId, settings.TargetCurrency),
                AppName = app.Name,
                Archived = app.Archived,
                Enabled = settings.Enabled,
                EnforceTargetAmount = settings.EnforceTargetAmount,
                StartDate = settings.StartDate,
                TargetCurrency = settings.TargetCurrency,
                MainImageUrl = settings.MainImageUrl == null ? null : await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), settings.MainImageUrl),
                Description = settings.Description,
                EndDate = settings.EndDate,
                TargetAmount = settings.TargetAmount,
                NotificationUrl = settings.NotificationUrl,
                Tagline = settings.Tagline,
                PerksTemplate = settings.PerksTemplate,
                DisqusEnabled = settings.DisqusEnabled,
                SoundsEnabled = settings.SoundsEnabled,
                DisqusShortname = settings.DisqusShortname,
                AnimationsEnabled = settings.AnimationsEnabled,
                ResetEveryAmount = settings.ResetEveryAmount,
                ResetEvery = resetEvery,
                IsRecurring = resetEvery != nameof(CrowdfundResetEvery.Never),
                UseAllStoreInvoices = app.TagAllInvoices,
                AppId = appId,
                SearchTerm = app.TagAllInvoices ? $"storeid:{app.StoreDataId}" : $"appid:{app.Id}",
                DisplayPerksRanking = settings.DisplayPerksRanking,
                DisplayPerksValue = settings.DisplayPerksValue,
                SortPerksByPopularity = settings.SortPerksByPopularity,
                Sounds = string.Join(Environment.NewLine, settings.Sounds),
                AnimationColors = string.Join(Environment.NewLine, settings.AnimationColors),
                FormId = settings.FormId
            };
            return View("Crowdfund/UpdateCrowdfund", vm);
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpPost("{appId}/settings/crowdfund")]
        public async Task<IActionResult> UpdateCrowdfund(string appId, UpdateCrowdfundViewModel vm,
            [FromForm] bool RemoveLogoFile = false)
        {
            var userId = GetUserId();
            if (userId is null)
                return NotFound();

            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            vm.AppId = app.Id;
            vm.TargetCurrency = await GetStoreDefaultCurrentIfEmpty(app.StoreDataId, vm.TargetCurrency);
            if (_currencies.GetCurrencyData(vm.TargetCurrency, false) == null)
                ModelState.AddModelError(nameof(vm.TargetCurrency), "Invalid currency");

            try
            {
                vm.PerksTemplate = AppService.SerializeTemplate(AppService.Parse(vm.PerksTemplate, true, true));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(nameof(vm.PerksTemplate), $"Invalid template: {ex.Message}");
            }
            if (vm.TargetAmount is decimal v && v == 0.0m)
            {
                vm.TargetAmount = null;
            }

            if (!vm.IsRecurring)
            {
                vm.ResetEvery = nameof(CrowdfundResetEvery.Never);
            }

            if (Enum.Parse<CrowdfundResetEvery>(vm.ResetEvery) != CrowdfundResetEvery.Never && !vm.StartDate.HasValue)
            {
                ModelState.AddModelError(nameof(vm.StartDate), "A start date is needed when the goal resets every X amount of time");
            }

            if (Enum.Parse<CrowdfundResetEvery>(vm.ResetEvery) != CrowdfundResetEvery.Never && vm.ResetEveryAmount <= 0)
            {
                ModelState.AddModelError(nameof(vm.ResetEveryAmount), "You must reset the goal at a minimum of 1");
            }

            if (vm.StartDate != null && vm.EndDate != null && DateTime.Compare((DateTime)vm.StartDate, (DateTime)vm.EndDate) > 0)
            {
                ModelState.AddModelError(nameof(vm.EndDate), "End date cannot be before start date");
            }

            if (vm.DisplayPerksRanking)
            {
                vm.SortPerksByPopularity = true;
            }

            var parsedSounds = vm.Sounds?.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None
            ).Select(s => s.Trim()).ToArray();
            if (vm.SoundsEnabled && (parsedSounds == null || !parsedSounds.Any()))
            {
                vm.SoundsEnabled = false;
                parsedSounds = new CrowdfundSettings().Sounds;
            }

            var parsedAnimationColors = vm.AnimationColors?.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None
            ).Select(s => s.Trim()).ToArray();
            if (vm.AnimationsEnabled && (parsedAnimationColors == null || !parsedAnimationColors.Any()))
            {
                vm.AnimationsEnabled = false;
                parsedAnimationColors = new CrowdfundSettings().AnimationColors;
            }

            UploadImageResultModel imageUpload = null;
            if (vm.MainImageFile != null)
            {
                imageUpload = await _fileService.UploadImage(vm.MainImageFile, userId);
                if (!imageUpload.Success)
                {
                    ModelState.AddModelError(nameof(vm.MainImageFile), imageUpload.Response);
                }
            }

            if (!ModelState.IsValid)
            {
                return View("Crowdfund/UpdateCrowdfund", vm);
            }

            app.Name = vm.AppName;
            app.Archived = vm.Archived;
            var newSettings = new CrowdfundSettings
            {
                Title = vm.Title,
                Enabled = vm.Enabled,
                EnforceTargetAmount = vm.EnforceTargetAmount,
                StartDate = vm.StartDate?.ToUniversalTime(),
                TargetCurrency = vm.TargetCurrency,
                Description = vm.Description,
                EndDate = vm.EndDate?.ToUniversalTime(),
                TargetAmount = vm.TargetAmount,
                MainImageUrl = app.GetSettings<CrowdfundSettings>()?.MainImageUrl,
                NotificationUrl = vm.NotificationUrl,
                Tagline = vm.Tagline,
                PerksTemplate = vm.PerksTemplate,
                DisqusEnabled = vm.DisqusEnabled,
                SoundsEnabled = vm.SoundsEnabled,
                DisqusShortname = vm.DisqusShortname,
                AnimationsEnabled = vm.AnimationsEnabled,
                ResetEveryAmount = vm.ResetEveryAmount,
                ResetEvery = Enum.Parse<CrowdfundResetEvery>(vm.ResetEvery),
                DisplayPerksValue = vm.DisplayPerksValue,
                DisplayPerksRanking = vm.DisplayPerksRanking,
                SortPerksByPopularity = vm.SortPerksByPopularity,
                Sounds = parsedSounds,
                AnimationColors = parsedAnimationColors,
                FormId = vm.FormId
            };

            if (imageUpload?.Success is true)
            {
                newSettings.MainImageUrl = new UnresolvedUri.FileIdUri(imageUpload.StoredFile.Id);
            }
            else if (RemoveLogoFile)
            {
                newSettings.MainImageUrl = null;
                vm.MainImageUrl = null;
                vm.MainImageFile = null;
            }

            app.TagAllInvoices = vm.UseAllStoreInvoices;
            app.SetSettings(newSettings);

            await _appService.UpdateOrCreateApp(app);

            _eventAggregator.Publish(new UIAppsController.AppUpdated
            {
                AppId = appId,
                StoreId = app.StoreDataId,
                Settings = newSettings
            });
            TempData[WellKnownTempData.SuccessMessage] = "App updated";
            return RedirectToAction(nameof(UpdateCrowdfund), new { appId });
        }

        private async Task<string> GetStoreDefaultCurrentIfEmpty(string storeId, string currency)
        {
            if (string.IsNullOrWhiteSpace(currency))
            {
                var store = await _storeRepository.FindStore(storeId);
                if (store == null)
                {
                    throw new Exception($"Could not find store with id {storeId}");
                }

                currency = store.GetStoreBlob().DefaultCurrency;
            }
            return currency.Trim().ToUpperInvariant();
        }

        private AppData GetCurrentApp() => HttpContext.GetAppData();

        private string GetUserId() => _userManager.GetUserId(User);

        private async Task<ViewCrowdfundViewModel> GetAppInfo(string appId)
        {
            var app = await _appService.GetApp(appId, CrowdfundAppType.AppType, true);
            if (app is null)
            {
                return null;
            }
            var info = (ViewCrowdfundViewModel)await _app.GetInfo(app);
            info.StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, app.StoreData.GetStoreBlob());
            info.HubPath = AppHub.GetHubPath(Request);
            info.SimpleDisplay = Request.Query.ContainsKey("simple");
            return info;
        }
    }
}
