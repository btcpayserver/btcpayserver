using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.Controllers
{
    public class UIPullPaymentController : Controller
    {
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly DisplayFormatter _displayFormatter;
        private readonly PullPaymentHostedService _pullPaymentHostedService;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly BTCPayNetworkJsonSerializerSettings _serializerSettings;
        private readonly IEnumerable<IPayoutHandler> _payoutHandlers;
        private readonly StoreRepository _storeRepository;

        public UIPullPaymentController(ApplicationDbContextFactory dbContextFactory,
            CurrencyNameTable currencyNameTable,
            DisplayFormatter displayFormatter,
            PullPaymentHostedService pullPaymentHostedService,
            BTCPayNetworkProvider networkProvider,
            BTCPayNetworkJsonSerializerSettings serializerSettings,
            IEnumerable<IPayoutHandler> payoutHandlers,
            StoreRepository storeRepository)
        {
            _dbContextFactory = dbContextFactory;
            _currencyNameTable = currencyNameTable;
            _displayFormatter = displayFormatter;
            _pullPaymentHostedService = pullPaymentHostedService;
            _serializerSettings = serializerSettings;
            _payoutHandlers = payoutHandlers;
            _storeRepository = storeRepository;
            _networkProvider = networkProvider;
        }

        [AllowAnonymous]
        [HttpGet("pull-payments/{pullPaymentId}")]
        public async Task<IActionResult> ViewPullPayment(string pullPaymentId)
        {
            using var ctx = _dbContextFactory.CreateContext();
            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp is null)
                return NotFound();

            var blob = pp.GetBlob();
            var store = await _storeRepository.FindStore(pp.StoreId);
            if (store is null)
                return NotFound();

            var storeBlob = store.GetStoreBlob();
            var payouts = (await ctx.Payouts.GetPayoutInPeriod(pp)
                    .OrderByDescending(o => o.Date)
                    .ToListAsync())
                .Select(o => new
                {
                    Entity = o,
                    Blob = o.GetBlob(_serializerSettings),
                    ProofBlob = _payoutHandlers.FindPayoutHandler(o.GetPaymentMethodId())?.ParseProof(o)
                });
            var cd = _currencyNameTable.GetCurrencyData(blob.Currency, false);
            var totalPaid = payouts.Where(p => p.Entity.State != PayoutState.Cancelled).Select(p => p.Blob.Amount).Sum();
            var amountDue = blob.Limit - totalPaid;

            ViewPullPaymentModel vm = new(pp, DateTimeOffset.UtcNow)
            {
                AmountCollected = totalPaid,
                AmountDue = amountDue,
                ClaimedAmount = amountDue,
                CurrencyData = cd,
                StartDate = pp.StartDate,
                LastRefreshed = DateTime.UtcNow,
                Payouts = payouts.Select(entity => new ViewPullPaymentModel.PayoutLine
                {
                    Id = entity.Entity.Id,
                    Amount = entity.Blob.Amount,
                    AmountFormatted = _displayFormatter.Currency(entity.Blob.Amount, blob.Currency),
                    Currency = blob.Currency,
                    Status = entity.Entity.State,
                    Destination = entity.Blob.Destination,
                    PaymentMethod = PaymentMethodId.Parse(entity.Entity.PaymentMethodId),
                    Link = entity.ProofBlob?.Link,
                    TransactionId = entity.ProofBlob?.Id
                }).ToList()
            };
            vm.IsPending &= vm.AmountDue > 0.0m;
            vm.StoreBranding = new StoreBrandingViewModel(storeBlob)
            {
                EmbeddedCSS = blob.View.EmbeddedCSS,
                CustomCSSLink = blob.View.CustomCSSLink
            };
            
            if (_pullPaymentHostedService.SupportsLNURL(blob))
            {
                var url = Url.Action("GetLNURLForPullPayment", "UILNURL", new { cryptoCode = _networkProvider.DefaultNetwork.CryptoCode, pullPaymentId = vm.Id }, Request.Scheme, Request.Host.ToString());
                vm.LnurlEndpoint = url != null ? new Uri(url) : null;
            }
            
            return View(nameof(ViewPullPayment), vm);
        }

        [HttpGet("stores/{storeId}/pull-payments/edit/{pullPaymentId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> EditPullPayment(string storeId, string pullPaymentId)
        {
            using var ctx = _dbContextFactory.CreateContext();
            Data.PullPaymentData pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp == null && !string.IsNullOrEmpty(pullPaymentId))
            {
                return NotFound();
            }

            var vm = new UpdatePullPaymentModel(pp);
            return View(vm);
        }

        [HttpPost("stores/{storeId}/pull-payments/edit/{pullPaymentId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> EditPullPayment(string storeId, string pullPaymentId, UpdatePullPaymentModel viewModel)
        {
            using var ctx = _dbContextFactory.CreateContext();

            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp == null && !string.IsNullOrEmpty(pullPaymentId))
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            var blob = pp.GetBlob();
            blob.Description = viewModel.Description ?? string.Empty;
            blob.Name = viewModel.Name ?? string.Empty;
            blob.View = new PullPaymentBlob.PullPaymentView()
            {
                Title = viewModel.Name ?? string.Empty,
                Description = viewModel.Description ?? string.Empty,
                CustomCSSLink = viewModel.CustomCSSLink,
                Email = null,
                EmbeddedCSS = viewModel.EmbeddedCSS,
            };

            pp.SetBlob(blob);
            ctx.PullPayments.Update(pp);
            await ctx.SaveChangesAsync();

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Pull payment updated successfully",
                Severity = StatusMessageModel.StatusSeverity.Success
            });

            return RedirectToAction(nameof(UIStorePullPaymentsController.PullPayments), "UIStorePullPayments", new { storeId, pullPaymentId });
        }

        [AllowAnonymous]
        [HttpPost("pull-payments/{pullPaymentId}/claim")]
        public async Task<IActionResult> ClaimPullPayment(string pullPaymentId, ViewPullPaymentModel vm, CancellationToken cancellationToken)
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp is null)
            {
                ModelState.AddModelError(nameof(pullPaymentId), "This pull payment does not exists");
            }

            if (string.IsNullOrEmpty(vm.Destination))
            {
                ModelState.AddModelError(nameof(vm.Destination), "Please provide a destination");
                return await ViewPullPayment(pullPaymentId);
            }

            var ppBlob = pp.GetBlob();
            var supported = ppBlob.SupportedPaymentMethods;
            PaymentMethodId paymentMethodId = null;
            IClaimDestination destination = null;
            if (string.IsNullOrEmpty(vm.SelectedPaymentMethod))
            {
                foreach (var pmId in supported)
                {
                    var handler = _payoutHandlers.FindPayoutHandler(pmId);
                    (IClaimDestination dst, string err) = handler == null
                        ? (null, "No payment handler found for this payment method")
                        : await handler.ParseAndValidateClaimDestination(pmId, vm.Destination, ppBlob, cancellationToken);
                    if (dst is not null && err is null)
                    {
                        paymentMethodId = pmId;
                        destination = dst;
                        break;
                    }
                }
            }
            else
            {
                paymentMethodId = supported.FirstOrDefault(id => vm.SelectedPaymentMethod == id.ToString());
                var payoutHandler = paymentMethodId is null ? null : _payoutHandlers.FindPayoutHandler(paymentMethodId);
                destination = payoutHandler is null ? null : (await payoutHandler.ParseAndValidateClaimDestination(paymentMethodId, vm.Destination, ppBlob, cancellationToken)).destination;
            }

            if (destination is null)
            {
                ModelState.AddModelError(nameof(vm.Destination), "Invalid destination or payment method");
                return await ViewPullPayment(pullPaymentId);
            }
            
            var amtError = ClaimRequest.IsPayoutAmountOk(destination, vm.ClaimedAmount == 0? null: vm.ClaimedAmount, paymentMethodId.CryptoCode, ppBlob.Currency);
            if (amtError.error is not null)
            {
                ModelState.AddModelError(nameof(vm.ClaimedAmount), amtError.error );
            }
            else if (amtError.amount is not null)
            {
                vm.ClaimedAmount = amtError.amount.Value;
            }

            if (!ModelState.IsValid)
            {
                return await ViewPullPayment(pullPaymentId);
            }

            var result = await _pullPaymentHostedService.Claim(new ClaimRequest
            {
                Destination = destination,
                PullPaymentId = pullPaymentId,
                Value = vm.ClaimedAmount,
                PaymentMethodId = paymentMethodId
            });

            if (result.Result != ClaimRequest.ClaimResult.Ok)
            {
                ModelState.AddModelError(
                    result.Result == ClaimRequest.ClaimResult.AmountTooLow ? nameof(vm.ClaimedAmount) : string.Empty,
                    ClaimRequest.GetErrorMessage(result.Result));
                return await ViewPullPayment(pullPaymentId);
            }

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"Your claim request of {_displayFormatter.Currency(vm.ClaimedAmount, ppBlob.Currency, DisplayFormatter.CurrencyFormat.Symbol)} to {vm.Destination} has been submitted and is awaiting {(result.PayoutData.State == PayoutState.AwaitingApproval ? "approval" : "payment")}.",
                Severity = StatusMessageModel.StatusSeverity.Success
            });

            return RedirectToAction(nameof(ViewPullPayment), new { pullPaymentId });
        }
    }
}
