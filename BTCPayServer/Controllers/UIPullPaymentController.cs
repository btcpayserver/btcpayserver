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
        private readonly PullPaymentHostedService _pullPaymentHostedService;
        private readonly BTCPayNetworkJsonSerializerSettings _serializerSettings;
        private readonly IEnumerable<IPayoutHandler> _payoutHandlers;

        public UIPullPaymentController(ApplicationDbContextFactory dbContextFactory,
            CurrencyNameTable currencyNameTable,
            PullPaymentHostedService pullPaymentHostedService,
            BTCPayNetworkJsonSerializerSettings serializerSettings,
            IEnumerable<IPayoutHandler> payoutHandlers)
        {
            _dbContextFactory = dbContextFactory;
            _currencyNameTable = currencyNameTable;
            _pullPaymentHostedService = pullPaymentHostedService;
            _serializerSettings = serializerSettings;
            _payoutHandlers = payoutHandlers;
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

            ViewPullPaymentModel vm = new (pp, DateTimeOffset.UtcNow)
            {
                AmountFormatted = _currencyNameTable.FormatCurrency(blob.Limit, blob.Currency),
                AmountCollected = totalPaid,
                AmountCollectedFormatted = _currencyNameTable.FormatCurrency(totalPaid, blob.Currency),
                AmountDue = amountDue,
                ClaimedAmount = amountDue,
                AmountDueFormatted = _currencyNameTable.FormatCurrency(amountDue, blob.Currency),
                CurrencyData = cd,
                StartDate = pp.StartDate,
                LastRefreshed = DateTime.UtcNow,
                Payouts = payouts
                          .Select(entity => new ViewPullPaymentModel.PayoutLine
                          {
                              Id = entity.Entity.Id,
                              Amount = entity.Blob.Amount,
                              AmountFormatted = _currencyNameTable.FormatCurrency(entity.Blob.Amount, blob.Currency),
                              Currency = blob.Currency,
                              Status = entity.Entity.State,
                              Destination = entity.Blob.Destination,
                              PaymentMethod = PaymentMethodId.Parse(entity.Entity.PaymentMethodId),
                              Link = entity.ProofBlob?.Link,
                              TransactionId = entity.ProofBlob?.Id
                          }).ToList()
            };
            vm.IsPending &= vm.AmountDue > 0.0m;
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
            using var ctx = _dbContextFactory.CreateContext();
            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp is null)
            {
                ModelState.AddModelError(nameof(pullPaymentId), "This pull payment does not exists");
            }

            var ppBlob = pp.GetBlob();

            var paymentMethodId = ppBlob.SupportedPaymentMethods.FirstOrDefault(id => vm.SelectedPaymentMethod == id.ToString());

            var payoutHandler = paymentMethodId is null ? null : _payoutHandlers.FindPayoutHandler(paymentMethodId);
            if (payoutHandler is null)
            {
                ModelState.AddModelError(nameof(vm.SelectedPaymentMethod), "Invalid destination with selected payment method");
                return await ViewPullPayment(pullPaymentId);
            }
            var destination = await payoutHandler.ParseAndValidateClaimDestination(paymentMethodId, vm.Destination, ppBlob, cancellationToken);
            if (destination.destination is null)
            {
                ModelState.AddModelError(nameof(vm.Destination), destination.error ?? "Invalid destination with selected payment method");
                return await ViewPullPayment(pullPaymentId);
            }

            if (vm.ClaimedAmount == 0)
            {
                ModelState.AddModelError(nameof(vm.ClaimedAmount), "Amount is required");
            }
            else
            {
                var amount = ppBlob.Currency == "SATS" ? new Money(vm.ClaimedAmount, MoneyUnit.Satoshi).ToUnit(MoneyUnit.BTC) : vm.ClaimedAmount;
                if (destination.destination.Amount != null && amount != destination.destination.Amount)
                {
                    var implied = _currencyNameTable.DisplayFormatCurrency(destination.destination.Amount.Value, paymentMethodId.CryptoCode);
                    var provided = _currencyNameTable.DisplayFormatCurrency(vm.ClaimedAmount, ppBlob.Currency);
                    ModelState.AddModelError(nameof(vm.ClaimedAmount), 
                        $"Amount implied in destination ({implied}) does not match the payout amount provided ({provided}).");
                }
            }

            if (!ModelState.IsValid)
            {
                return await ViewPullPayment(pullPaymentId);
            }

            var result = await _pullPaymentHostedService.Claim(new ClaimRequest()
            {
                Destination = destination.destination,
                PullPaymentId = pullPaymentId,
                Value = vm.ClaimedAmount,
                PaymentMethodId = paymentMethodId
            });

            if (result.Result != ClaimRequest.ClaimResult.Ok)
            {
                if (result.Result == ClaimRequest.ClaimResult.AmountTooLow)
                {
                    ModelState.AddModelError(nameof(vm.ClaimedAmount), ClaimRequest.GetErrorMessage(result.Result));
                }
                else
                {
                    ModelState.AddModelError(string.Empty, ClaimRequest.GetErrorMessage(result.Result));
                }
                return await ViewPullPayment(pullPaymentId);
            }
            
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"Your claim request of {_currencyNameTable.DisplayFormatCurrency(vm.ClaimedAmount, ppBlob.Currency)} to {vm.Destination} has been submitted and is awaiting {(result.PayoutData.State == PayoutState.AwaitingApproval? "approval": "payment")}.",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            
            return RedirectToAction(nameof(ViewPullPayment), new { pullPaymentId });
        }
    }
}
