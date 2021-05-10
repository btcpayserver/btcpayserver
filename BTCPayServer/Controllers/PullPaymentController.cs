using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Controllers
{
    [AllowAnonymous]
    public class PullPaymentController : Controller
    {
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly PullPaymentHostedService _pullPaymentHostedService;
        private readonly BTCPayNetworkJsonSerializerSettings _serializerSettings;
        private readonly IEnumerable<IPayoutHandler> _payoutHandlers;

        public PullPaymentController(ApplicationDbContextFactory dbContextFactory,
            BTCPayNetworkProvider networkProvider,
            CurrencyNameTable currencyNameTable,
            PullPaymentHostedService pullPaymentHostedService,
            BTCPayNetworkJsonSerializerSettings serializerSettings,
            IEnumerable<IPayoutHandler> payoutHandlers)
        {
            _dbContextFactory = dbContextFactory;
            _networkProvider = networkProvider;
            _currencyNameTable = currencyNameTable;
            _pullPaymentHostedService = pullPaymentHostedService;
            _serializerSettings = serializerSettings;
            _payoutHandlers = payoutHandlers;
        }
        
        [Route("pull-payments/{pullPaymentId}")]
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
                               ProofBlob = _payoutHandlers.FirstOrDefault(handler => handler.CanHandle(o.GetPaymentMethodId()))?.ParseProof(o)
                           });
            var cd = _currencyNameTable.GetCurrencyData(blob.Currency, false);
            var totalPaid = payouts.Where(p => p.Entity.State != PayoutState.Cancelled).Select(p => p.Blob.Amount).Sum();
            var amountDue = blob.Limit - totalPaid;

            ViewPullPaymentModel vm = new ViewPullPaymentModel(pp, DateTimeOffset.UtcNow)
            {
                AmountFormatted = _currencyNameTable.FormatCurrency(blob.Limit, blob.Currency),
                AmountCollected = totalPaid,
                AmountCollectedFormatted = _currencyNameTable.FormatCurrency(totalPaid, blob.Currency),
                AmountDue = amountDue,
                ClaimedAmount = amountDue,
                AmountDueFormatted = _currencyNameTable.FormatCurrency(amountDue, blob.Currency),
                CurrencyData = cd,
                StartDate = pp.StartDate,
                LastRefreshed = DateTime.Now,
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

        [Route("pull-payments/{pullPaymentId}/claim")]
        [HttpPost]
        public async Task<IActionResult> ClaimPullPayment(string pullPaymentId, ViewPullPaymentModel vm)
        {
            using var ctx = _dbContextFactory.CreateContext();
            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp is null)
            {
                ModelState.AddModelError(nameof(pullPaymentId), "This pull payment does not exists");
            }
            
            var ppBlob = pp.GetBlob();
            
            var paymentMethodId = ppBlob.SupportedPaymentMethods.FirstOrDefault(id => vm.SelectedPaymentMethod == id.ToString());
            
            var network = paymentMethodId is null? null:  _networkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            var payoutHandler = paymentMethodId is null? null: _payoutHandlers.FirstOrDefault(handler => handler.CanHandle(paymentMethodId));
            var destination = await payoutHandler?.ParseClaimDestination(paymentMethodId, vm.Destination);
            if (destination.Item1 is null)
            {
                ModelState.AddModelError(nameof(vm.Destination), $"Invalid destination with selected payment method");
            }
            if (vm.ClaimedAmount == 0 && destination.Item2 != null)
            {
                vm.ClaimedAmount  = destination.Item2.Value;
            }
            else if (vm.ClaimedAmount != 0 && destination.Item2 != null && vm.ClaimedAmount != destination.Item2)
            {
                ModelState.AddModelError(nameof(vm.ClaimedAmount),
                    $"Amount is implied in destination ({destination.Item2}) that does not match the payout amount provided {vm.ClaimedAmount})");
            }

            if (!ModelState.IsValid)
            {
                return await ViewPullPayment(pullPaymentId);
            }

            var result = await _pullPaymentHostedService.Claim(new ClaimRequest()
            {
                Destination = destination.Item1,
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
            else
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = $"Your claim request of {_currencyNameTable.DisplayFormatCurrency(vm.ClaimedAmount, ppBlob.Currency)} to {vm.Destination} has been submitted and is awaiting approval.",
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
            }
            return RedirectToAction(nameof(ViewPullPayment), new { pullPaymentId = pullPaymentId });
        }
    }
}
