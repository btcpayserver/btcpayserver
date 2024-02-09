#nullable enable
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Plugins.PointOfSale;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Rates;
using System.Text.RegularExpressions;
using System;
using BTCPayServer.Services.Stores;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Identity;
using BTCPayServer.Client.Models;
using Org.BouncyCastle.Ocsp;
using BTCPayServer.NTag424;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using BTCPayServer.Services;
using BTCPayServer.HostedServices;
using System.Threading;
using BTCPayServer.Plugins.BoltcardFactory.ViewModels;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Models;

namespace BTCPayServer.Plugins.BoltcardFactory.Controllers
{
    [AutoValidateAntiforgeryToken]
    [Route("apps")]
    public class UIBoltcardFactoryController : Controller
    {
        private readonly IEnumerable<IPayoutHandler> _payoutHandlers;
        private readonly CurrencyNameTable _currencies;
        private readonly AppService _appService;
        private readonly StoreRepository _storeRepository;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly IAuthorizationService _authorizationService;
        private readonly SettingsRepository _settingsRepository;
        private readonly BTCPayServerEnvironment _env;
        private readonly PullPaymentHostedService _ppService;
        private readonly ApplicationDbContextFactory _dbContextFactory;

        public UIBoltcardFactoryController(
            IEnumerable<IPayoutHandler> payoutHandlers,
            CurrencyNameTable currencies,
            AppService appService,
            StoreRepository storeRepository,
            CurrencyNameTable currencyNameTable,
            IAuthorizationService authorizationService,
            SettingsRepository settingsRepository,
            BTCPayServerEnvironment env,
            PullPaymentHostedService ppService,
            ApplicationDbContextFactory dbContextFactory)
        {
            _payoutHandlers = payoutHandlers;
            _currencies = currencies;
            _appService = appService;
            _storeRepository = storeRepository;
            _currencyNameTable = currencyNameTable;
            _authorizationService = authorizationService;
            _settingsRepository = settingsRepository;
            _env = env;
            _ppService = ppService;
            _dbContextFactory = dbContextFactory;
        }
        public Data.StoreData CurrentStore => HttpContext.GetStoreData();
        private AppData GetCurrentApp() => HttpContext.GetAppData();
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("{appId}/settings/boltcardfactory")]
        public async Task<IActionResult> UpdateBoltcardFactory(string appId)
        {
            if (CurrentStore is null || GetCurrentApp() is null)
                return NotFound();

            var paymentMethods = await _payoutHandlers.GetSupportedPaymentMethods(CurrentStore);
            if (!paymentMethods.Any())
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = "You must enable at least one payment method before creating a pull payment.",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(UIStoresController.Dashboard), "UIStores", new { storeId = CurrentStore.Id });
            }

            var req = GetCurrentApp().GetSettings<CreatePullPaymentRequest>();
            return base.View($"{BoltcardFactoryPlugin.ViewsDirectory}/UpdateBoltcardFactory.cshtml", CreateViewModel(paymentMethods, req));
        }

        private static NewPullPaymentModel CreateViewModel(List<PaymentMethodId> paymentMethods, CreatePullPaymentRequest req)
        {
            return new NewPullPaymentModel
            {
                Name = req.Name,
                Currency = req.Currency,
                Amount = req.Amount,
                AutoApproveClaims = req.AutoApproveClaims,
                Description = req.Description,
                PaymentMethods = req.PaymentMethods,
                BOLT11Expiration = req.BOLT11Expiration?.TotalDays is double v ? (long)v : 30, 
                EmbeddedCSS = req.EmbeddedCSS,
                CustomCSSLink = req.CustomCSSLink,
                PaymentMethodItems =
                                paymentMethods.Select(id => new SelectListItem(id.ToPrettyString(), id.ToString(), true))
            };
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpPost("{appId}/settings/boltcardfactory")]
        public async Task<IActionResult> UpdateBoltcardFactory(string appId, NewPullPaymentModel model)
        {
            if (CurrentStore is null)
                return NotFound();
            var storeId = CurrentStore.Id;
            var paymentMethodOptions = await _payoutHandlers.GetSupportedPaymentMethods(CurrentStore);
            model.PaymentMethodItems =
                paymentMethodOptions.Select(id => new SelectListItem(id.ToPrettyString(), id.ToString(), true));
            model.Name ??= string.Empty;
            model.Currency = model.Currency?.ToUpperInvariant()?.Trim() ?? String.Empty;
            model.PaymentMethods ??= new List<string>();

            if (!model.PaymentMethods.Any())
            {
                // Since we assign all payment methods to be selected by default above we need to update 
                // them here to reflect user's selection so that they can correct their mistake
                model.PaymentMethodItems =
                    paymentMethodOptions.Select(id => new SelectListItem(id.ToPrettyString(), id.ToString(), false));
                ModelState.AddModelError(nameof(model.PaymentMethods), "You need at least one payment method");
            }
            if (_currencyNameTable.GetCurrencyData(model.Currency, false) is null)
            {
                ModelState.AddModelError(nameof(model.Currency), "Invalid currency");
            }
            if (model.Amount <= 0.0m)
            {
                ModelState.AddModelError(nameof(model.Amount), "The amount should be more than zero");
            }
            if (model.Name.Length > 50)
            {
                ModelState.AddModelError(nameof(model.Name), "The name should be maximum 50 characters.");
            }

            var selectedPaymentMethodIds = model.PaymentMethods.Select(PaymentMethodId.Parse).ToArray();
            if (!selectedPaymentMethodIds.All(id => selectedPaymentMethodIds.Contains(id)))
            {
                ModelState.AddModelError(nameof(model.Name), "Not all payment methods are supported");
            }
            if (!ModelState.IsValid)
                return View(model);
            model.AutoApproveClaims = model.AutoApproveClaims && (await
                _authorizationService.AuthorizeAsync(User, CurrentStore.Id, Policies.CanCreatePullPayments)).Succeeded;

            var req = new CreatePullPaymentRequest()
            {
                Name = model.Name,
                Description = model.Description,
                Currency = model.Currency,
                CustomCSSLink = model.CustomCSSLink,
                Amount = model.Amount,
                AutoApproveClaims = model.AutoApproveClaims,
                EmbeddedCSS = model.EmbeddedCSS,
                BOLT11Expiration = TimeSpan.FromDays(model.BOLT11Expiration),
                PaymentMethods = model.PaymentMethods.ToArray()
            };
            var app = GetCurrentApp();
            app.SetSettings(req);
            await _appService.UpdateOrCreateApp(app);
            var paymentMethods = await _payoutHandlers.GetSupportedPaymentMethods(CurrentStore);
            this.TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Pull payment request created",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            return View($"{BoltcardFactoryPlugin.ViewsDirectory}/UpdateBoltcardFactory.cshtml", CreateViewModel(paymentMethods, req));
        }
        private async Task<string?> GetStoreDefaultCurrentIfEmpty(string storeId, string? currency)
        {
            if (string.IsNullOrWhiteSpace(currency))
            {
                currency = (await _storeRepository.FindStore(storeId))?.GetStoreBlob()?.DefaultCurrency;
            }
            return currency?.Trim().ToUpperInvariant();
        }
        private int[] ListSplit(string list, string separator = ",")
        {
            if (string.IsNullOrEmpty(list))
            {
                return Array.Empty<int>();
            }

            // Remove all characters except numeric and comma
            Regex charsToDestroy = new Regex(@"[^\d|\" + separator + "]");
            list = charsToDestroy.Replace(list, "");

            return list.Split(separator, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        }
        [HttpGet("/apps/{appId}/boltcardfactory")]
        [DomainMappingConstraint(BoltcardFactoryPlugin.AppType)]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> ViewBoltcardFactory(string appId)
        {
            var vm = new ViewBoltcardFactoryViewModel();
            vm.SetupDeepLink = $"boltcard://program?url={GetBoltcardDeeplinkUrl(appId, OnExistingBehavior.UpdateVersion)}";
            vm.ResetDeepLink = $"boltcard://reset?url={GetBoltcardDeeplinkUrl(appId, OnExistingBehavior.KeepVersion)}";
            return View($"{BoltcardFactoryPlugin.ViewsDirectory}/ViewBoltcardFactory.cshtml", vm);
        }

        private string GetBoltcardDeeplinkUrl(string appId, OnExistingBehavior onExisting)
        {
            var registerUrl = Url.Action(nameof(UIBoltcardFactoryController.RegisterBoltcard), "UIBoltcardFactory",
                            new
                            {
                                appId = appId,
                                onExisting = onExisting.ToString()
                            }, Request.Scheme, Request.Host.ToString());
            registerUrl = Uri.EscapeDataString(registerUrl!);
            return registerUrl;
        }

        [HttpPost]
        [Route("~/apps/{appId}/boltcards")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterBoltcard(string appId, RegisterBoltcardRequest request, string? onExisting = null)
        {
            var app = GetCurrentApp();
            if (app?.AppType != BoltcardFactoryPlugin.AppType)
                return NotFound();
            
            var issuerKey = await _settingsRepository.GetIssuerKey(_env);

            // LNURLW is used by deeplinks
            if (request?.LNURLW is not null)
            {
                if (request.UID is not null)
                {
                    ModelState.AddModelError(nameof(request.LNURLW), "You should pass either LNURLW or UID but not both");
                    return this.CreateValidationError(ModelState);
                }
                var p = ExtractP(request.LNURLW);
                if (p is null)
                {
                    ModelState.AddModelError(nameof(request.LNURLW), "The LNURLW should contains a 'p=' parameter");
                    return this.CreateValidationError(ModelState);
                }
                if (issuerKey.TryDecrypt(p) is not BoltcardPICCData picc)
                {
                    ModelState.AddModelError(nameof(request.LNURLW), "The LNURLW 'p=' parameter cannot be decrypted");
                    return this.CreateValidationError(ModelState);
                }
                request.UID = picc.Uid;
            }

            if (request?.UID is null || request.UID.Length != 7)
            {
                ModelState.AddModelError(nameof(request.UID), "The UID is required and should be 7 bytes");
                return this.CreateValidationError(ModelState);
            }

            // Passing onExisting as a query parameter is used by deeplink
            request.OnExisting = onExisting switch
            {
                nameof(OnExistingBehavior.UpdateVersion) => OnExistingBehavior.UpdateVersion,
                nameof(OnExistingBehavior.KeepVersion) => OnExistingBehavior.KeepVersion,
                _ => request.OnExisting
            };

            int version;
            string ppId;
            var registration = await _dbContextFactory.GetBoltcardRegistration(issuerKey, request.UID);
            
            if (request.OnExisting == OnExistingBehavior.UpdateVersion)
            {
                var req = app.GetSettings<CreatePullPaymentRequest>();
                ppId = await _ppService.CreatePullPayment(app.StoreDataId, req);
                version = await _dbContextFactory.LinkBoltcardToPullPayment(ppId, issuerKey, request.UID, request.OnExisting);
            }
            // If it's a reset, do not create a new pull payment
            else
            {
                if (registration?.PullPaymentId is null)
                {
                    ModelState.AddModelError(nameof(request.UID), "This card isn't registered");
                    return this.CreateValidationError(ModelState);
                }
                ppId = registration.PullPaymentId;
                version = registration.Version;
            }

            var keys = issuerKey.CreatePullPaymentCardKey(request.UID, version, ppId).DeriveBoltcardKeys(issuerKey);
            var boltcardUrl = Url.Action(nameof(UIBoltcardController.GetWithdrawRequest), "UIBoltcard");
            boltcardUrl = Request.GetAbsoluteUri(boltcardUrl);
            boltcardUrl = Regex.Replace(boltcardUrl, "^https?://", "lnurlw://");

            var resp = new RegisterBoltcardResponse()
            {
                LNURLW = boltcardUrl,
                Version = version,
                K0 = Encoders.Hex.EncodeData(keys.AppMasterKey.ToBytes()).ToUpperInvariant(),
                K1 = Encoders.Hex.EncodeData(keys.EncryptionKey.ToBytes()).ToUpperInvariant(),
                K2 = Encoders.Hex.EncodeData(keys.AuthenticationKey.ToBytes()).ToUpperInvariant(),
                K3 = Encoders.Hex.EncodeData(keys.K3.ToBytes()).ToUpperInvariant(),
                K4 = Encoders.Hex.EncodeData(keys.K4.ToBytes()).ToUpperInvariant(),
            };

            return Ok(resp);
        }

        private string? ExtractP(string? url)
        {
            if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return null;
            int num = uri.AbsoluteUri.IndexOf('?');
            if (num == -1)
                return null;
            string input = uri.AbsoluteUri.Substring(num);
            Match match = Regex.Match(input, "p=([a-f0-9A-F]{32})");
            if (!match.Success)
                return null;
            return match.Groups[1].Value;
        }
    }
}
