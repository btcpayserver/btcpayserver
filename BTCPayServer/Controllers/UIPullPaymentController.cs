using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Controllers
{
    public partial class UIPullPaymentController : Controller
    {
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly DisplayFormatter _displayFormatter;
        private readonly UriResolver _uriResolver;
        private readonly PullPaymentHostedService _pullPaymentHostedService;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly BTCPayNetworkJsonSerializerSettings _serializerSettings;
        private readonly PayoutMethodHandlerDictionary _payoutHandlers;
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayServerEnvironment _env;
        private readonly SettingsRepository _settingsRepository;

        public IStringLocalizer StringLocalizer { get; }

        public UIPullPaymentController(
            ApplicationDbContextFactory dbContextFactory,
            CurrencyNameTable currencyNameTable,
            DisplayFormatter displayFormatter,
            UriResolver uriResolver,
            PullPaymentHostedService pullPaymentHostedService,
            BTCPayNetworkProvider networkProvider,
            BTCPayNetworkJsonSerializerSettings serializerSettings,
            PayoutMethodHandlerDictionary payoutHandlers,
            StoreRepository storeRepository,
            BTCPayServerEnvironment env,
            IStringLocalizer stringLocalizer,
            SettingsRepository settingsRepository)
        {
            _dbContextFactory = dbContextFactory;
            _currencyNameTable = currencyNameTable;
            _displayFormatter = displayFormatter;
            _uriResolver = uriResolver;
            _pullPaymentHostedService = pullPaymentHostedService;
            _serializerSettings = serializerSettings;
            _payoutHandlers = payoutHandlers;
            _storeRepository = storeRepository;
            _env = env;
            _settingsRepository = settingsRepository;
            _networkProvider = networkProvider;
            StringLocalizer = stringLocalizer;
        }

        [AllowAnonymous]
        [HttpGet("pull-payments/{pullPaymentId}")]
        public async Task<IActionResult> ViewPullPayment(string pullPaymentId)
        {
            using var ctx = _dbContextFactory.CreateContext();

            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp is null)
                return NotFound();

            var store = await _storeRepository.FindStore(pp.StoreId);
            if (store is null)
                return NotFound();

            var storeBlob = store.GetStoreBlob();

            var payouts = await ctx.Payouts
                .Where(p => p.PullPaymentDataId == pp.Id)
                .OrderByDescending(o => o.Date)
                .ToListAsync();

            var payoutsWithDetails = payouts
                .Select(o => new
                {
                    Entity = o,
                    Blob = o.GetBlob(_serializerSettings),
                    ProofBlob = _payoutHandlers.TryGet(o.GetPayoutMethodId())?.ParseProof(o)
                }).ToList();

            var currencyData = _currencyNameTable.GetCurrencyData(pp.Currency, false);

            var totalPaid = payoutsWithDetails
                .Where(p => p.Entity.State != PayoutState.Cancelled)
                .Sum(p => p.Entity.OriginalAmount);

            var amountDue = pp.Limit - totalPaid;

            var vm = new ViewPullPaymentModel(pp, DateTimeOffset.UtcNow)
            {
                AmountCollected = totalPaid,
                AmountDue = amountDue,
                ClaimedAmount = amountDue,
                CurrencyData = currencyData,
                StartDate = pp.StartDate,
                LastRefreshed = DateTime.UtcNow,
                Payouts = payoutsWithDetails.Select(p => new ViewPullPaymentModel.PayoutLine
                {
                    Id = p.Entity.Id,
                    Amount = p.Entity.OriginalAmount,
                    AmountFormatted = _displayFormatter.Currency(p.Entity.OriginalAmount, p.Entity.OriginalCurrency),
                    Currency = p.Entity.OriginalCurrency,
                    Status = p.Entity.State,
                    Destination = p.Blob.Destination,
                    PaymentMethod = PaymentMethodId.Parse(p.Entity.PayoutMethodId),
                    Link = p.ProofBlob?.Link,
                    TransactionId = p.ProofBlob?.Id
                }).ToList()
            };

            // Ensure IsPending reflects if there's an amount due
            vm.IsPending &= vm.AmountDue > 0.0m;

            vm.StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob);

            if (_pullPaymentHostedService.SupportsLNURL(pp))
            {
                var url = Url.Action(
                    nameof(UILNURLController.GetLNURLForPullPayment),
                    "UILNURL",
                    new { cryptoCode = _networkProvider.DefaultNetwork.CryptoCode, pullPaymentId = vm.Id },
                    Request.Scheme,
                    Request.Host.ToString());

                vm.LnurlEndpoint = url != null ? new Uri(url) : null;
                vm.SetupDeepLink = $"boltcard://program?url={GetBoltcardDeeplinkUrl(vm, OnExistingBehavior.UpdateVersion)}";
                vm.ResetDeepLink = $"boltcard://reset?url={GetBoltcardDeeplinkUrl(vm, OnExistingBehavior.KeepVersion)}";
            }

            return View(nameof(ViewPullPayment), vm);
        }

        private string GetBoltcardDeeplinkUrl(ViewPullPaymentModel vm, OnExistingBehavior onExisting)
        {
            var registerUrl = Url.Action(
                nameof(GreenfieldPullPaymentController.RegisterBoltcard),
                "GreenfieldPullPayment",
                new
                {
                    pullPaymentId = vm.Id,
                    onExisting = onExisting.ToString()
                },
                Request.Scheme,
                Request.Host.ToString());

            return Uri.EscapeDataString(registerUrl);
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("stores/{storeId}/pull-payments/edit/{pullPaymentId}")]
        public async Task<IActionResult> EditPullPayment(string storeId, string pullPaymentId)
        {
            using var ctx = _dbContextFactory.CreateContext();

            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp == null && !string.IsNullOrEmpty(pullPaymentId))
                return NotFound();

            var vm = new UpdatePullPaymentModel(pp);
            return View(vm);
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpPost("stores/{storeId}/pull-payments/edit/{pullPaymentId}")]
        public async Task<IActionResult> EditPullPayment(string storeId, string pullPaymentId, UpdatePullPaymentModel viewModel)
        {
            using var ctx = _dbContextFactory.CreateContext();

            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp == null && !string.IsNullOrEmpty(pullPaymentId))
                return NotFound();

            if (!ModelState.IsValid)
                return View(viewModel);

            var blob = pp.GetBlob();
            blob.Description = viewModel.Description ?? string.Empty;
            blob.Name = viewModel.Name ?? string.Empty;
            blob.View = new PullPaymentBlob.PullPaymentView
            {
                Title = viewModel.Name ?? string.Empty,
                Description = viewModel.Description ?? string.Empty,
                Email = null
            };

            pp.SetBlob(blob);
            ctx.PullPayments.Update(pp);
            await ctx.SaveChangesAsync();

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = StringLocalizer["Pull payment updated successfully"],
                Severity = StatusMessageModel.StatusSeverity.Success
            });

            return RedirectToAction(nameof(UIStorePullPaymentsController.PullPayments), "UIStorePullPayments", new { storeId, pullPaymentId });
        }

        [AllowAnonymous]
        [HttpPost("pull-payments/{pullPaymentId}")]
        public async Task<IActionResult> ClaimPullPayment(string pullPaymentId, ViewPullPaymentModel vm, CancellationToken cancellationToken)
        {
            if (vm.ClaimedAmount == 0)
                vm.ClaimedAmount = null;

            await using var ctx = _dbContextFactory.CreateContext();

            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp == null)
            {
                ModelState.AddModelError(nameof(pullPaymentId), StringLocalizer["This pull payment does not exist"]);
                return await ViewPullPayment(pullPaymentId);
            }

            if (string.IsNullOrEmpty(vm.Destination))
            {
                ModelState.AddModelError(nameof(vm.Destination), StringLocalizer["Please provide a destination"]);
                return await ViewPullPayment(pullPaymentId);
            }

            var ppBlob = pp.GetBlob();
            var supported = ppBlob.SupportedPayoutMethods;

            var (payoutMethodId, destination, payoutHandler, error) = await TryParsePayoutMethodAndDestination(vm, supported, ppBlob, cancellationToken);

            if (destination == null)
            {
                ModelState.AddModelError(nameof(vm.Destination), error ?? StringLocalizer["Invalid destination or payment method"]);
                return await ViewPullPayment(pullPaymentId);
            }

            var claimedAmountResult = ClaimRequest.GetClaimedAmount(destination, vm.ClaimedAmount, payoutHandler.Currency, pp.Currency);

            switch (claimedAmountResult)
            {
                case ClaimRequest.ClaimedAmountResult.Error err:
                    ModelState.AddModelError(nameof(vm.ClaimedAmount), err.Message);
                    break;
                case ClaimRequest.ClaimedAmountResult.Success succ:
                    vm.ClaimedAmount = succ.Amount;
                    break;
            }

            if (!ModelState.IsValid)
                return await ViewPullPayment(pullPaymentId);

            var claimResult = await _pullPaymentHostedService.Claim(new ClaimRequest
            {
                Destination = destination,
                PullPaymentId = pullPaymentId,
                ClaimedAmount = vm.ClaimedAmount,
                PayoutMethodId = payoutMethodId,
                StoreId = pp.StoreId
            });

            if (claimResult.Result != ClaimRequest.ClaimResult.Ok)
            {
                ModelState.AddModelError(
                    claimResult.Result == ClaimRequest.ClaimResult.AmountTooLow ? nameof(vm.ClaimedAmount) : string.Empty,
                    ClaimRequest.GetErrorMessage(claimResult.Result));
                return await ViewPullPayment(pullPaymentId);
            }

            return RedirectToAction(nameof(ViewPullPayment), new { pullPaymentId });
        }

        private async Task<(string payoutMethodId, string destination, IPayoutHandler payoutHandler, string error)>
            TryParsePayoutMethodAndDestination(ViewPullPaymentModel vm, string[] supportedMethods, PullPaymentBlob ppBlob, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(vm.PayoutMethod))
            {
                // If no payout method specified, fallback to the first supported
                var firstSupported = supportedMethods?.FirstOrDefault();
                if (firstSupported == null)
                    return (null, null, null, StringLocalizer["No supported payout methods found"]);

                var handler = _payoutHandlers.TryGet(firstSupported);
                if (handler == null)
                    return (null, null, null, StringLocalizer["Unsupported payout method"]);

                var dest = await handler.ParseClaimDestination(vm.Destination, cancellationToken);
                if (dest == null)
                    return (null, null, null, StringLocalizer["Invalid destination for payout method"]);

                return (firstSupported, dest, handler, null);
            }
            else
            {
                var handler = _payoutHandlers.TryGet(vm.PayoutMethod);
                if (handler == null)
                    return (null, null, null, StringLocalizer["Unsupported payout method"]);

                var dest = await handler.ParseClaimDestination(vm.Destination, cancellationToken);
                if (dest == null)
                    return (null, null, null, StringLocalizer["Invalid destination for payout method"]);

                if (!supportedMethods.Contains(vm.PayoutMethod))
                    return (null, null, null, StringLocalizer["Payout method not supported by this pull payment"]);

                return (vm.PayoutMethod, dest, handler, null);
            }
        }
    }
}
