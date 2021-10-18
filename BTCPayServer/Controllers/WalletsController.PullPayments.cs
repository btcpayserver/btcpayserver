using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Common;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Views;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using PayoutData = BTCPayServer.Data.PayoutData;

namespace BTCPayServer.Controllers
{
    public partial class WalletsController
    {
        [HttpGet("{walletId}/pull-payments/new")]
        public IActionResult NewPullPayment([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId)
        {
            
            if (GetDerivationSchemeSettings(walletId) == null)
                return NotFound();
            var storeMethods = CurrentStore.GetSupportedPaymentMethods(NetworkProvider).Select(method => method.PaymentId).ToList();
            var paymentMethodOptions = _payoutHandlers.GetSupportedPaymentMethods(storeMethods);
            return View(new NewPullPaymentModel
            {
                Name = "",
                Currency = "BTC",
                CustomCSSLink = "",
                EmbeddedCSS = "",
                PaymentMethodItems = paymentMethodOptions.Select(id => new SelectListItem(id.ToPrettyString(), id.ToString(), true))
            });
        }
        
        [HttpPost("{walletId}/pull-payments/new")]
        public async Task<IActionResult> NewPullPayment([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, NewPullPaymentModel model)
        {
            if (GetDerivationSchemeSettings(walletId) == null)
                return NotFound();

            var storeMethods = CurrentStore.GetSupportedPaymentMethods(NetworkProvider).Select(method => method.PaymentId).ToList();
            var paymentMethodOptions = _payoutHandlers.GetSupportedPaymentMethods(storeMethods);
            model.PaymentMethodItems =
                paymentMethodOptions.Select(id => new SelectListItem(id.ToPrettyString(), id.ToString(), true));
            model.Name ??= string.Empty;
            model.Currency = model.Currency.ToUpperInvariant().Trim();
            if (!model.PaymentMethods.Any())
            {
                ModelState.AddModelError(nameof(model.PaymentMethods), "You need at least one payment method");
            }
            if (_currencyTable.GetCurrencyData(model.Currency, false) is null)
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
            await _pullPaymentService.CreatePullPayment(new HostedServices.CreatePullPayment()
            {
                Name = model.Name,
                Amount = model.Amount,
                Currency = model.Currency,
                StoreId = walletId.StoreId,
                PaymentMethodIds = selectedPaymentMethodIds,
                EmbeddedCSS = model.EmbeddedCSS,
                CustomCSSLink = model.CustomCSSLink
            });
            this.TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Pull payment request created",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction(nameof(PullPayments), new { walletId = walletId.ToString() });
        }
        
        [HttpGet("{walletId}/pull-payments")]
        public async Task<IActionResult> PullPayments(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId)
        {
            using var ctx = this._dbContextFactory.CreateContext();
            var now = DateTimeOffset.UtcNow;
            var storeId = walletId.StoreId;
            var pps = await ctx.PullPayments.Where(p => p.StoreId == storeId && !p.Archived)
                                      .OrderByDescending(p => p.StartDate)
                                      .Select(o => new
                                      {
                                          PullPayment = o,
                                          Awaiting = o.Payouts
                                                        .Where(p => p.State == PayoutState.AwaitingPayment || p.State == PayoutState.AwaitingApproval),
                                          Completed = o.Payouts
                                                        .Where(p => p.State == PayoutState.Completed || p.State == PayoutState.InProgress)
                                      })
                                      .ToListAsync();

            var vm = new PullPaymentsModel
                { HasDerivationSchemeSettings = GetDerivationSchemeSettings(walletId) != null };

            foreach (var o in pps)
            {
                var pp = o.PullPayment;
                var totalCompleted = o.Completed.Where(o => o.IsInPeriod(pp, now))
                                                .Select(o => o.GetBlob(_jsonSerializerSettings).Amount).Sum();
                var totalAwaiting = o.Awaiting.Where(o => o.IsInPeriod(pp, now))
                                              .Select(o => o.GetBlob(_jsonSerializerSettings).Amount).Sum();
                var ppBlob = pp.GetBlob();
                var ni = _currencyTable.GetCurrencyData(ppBlob.Currency, true);
                var nfi = _currencyTable.GetNumberFormatInfo(ppBlob.Currency, true);
                var period = pp.GetPeriod(now);
                vm.PullPayments.Add(new PullPaymentsModel.PullPaymentModel()
                {
                    StartDate = pp.StartDate,
                    EndDate = pp.EndDate,
                    Id = pp.Id,
                    Name = ppBlob.Name,
                    Progress = new PullPaymentsModel.PullPaymentModel.ProgressModel()
                    {
                        CompletedPercent = (int)(totalCompleted / ppBlob.Limit * 100m),
                        AwaitingPercent = (int)(totalAwaiting / ppBlob.Limit * 100m),
                        Awaiting = totalAwaiting.RoundToSignificant(ni.Divisibility).ToString("C", nfi),
                        Completed = totalCompleted.RoundToSignificant(ni.Divisibility).ToString("C", nfi),
                        Limit = _currencyTable.DisplayFormatCurrency(ppBlob.Limit, ppBlob.Currency),
                        ResetIn = period?.End is DateTimeOffset nr ? ZeroIfNegative(nr - now).TimeString() : null,
                        EndIn = pp.EndDate is DateTimeOffset end ? ZeroIfNegative(end - now).TimeString() : null
                    }
                });
            }
            return View(vm);
        }
        public TimeSpan ZeroIfNegative(TimeSpan time)
        {
            if (time < TimeSpan.Zero)
                time = TimeSpan.Zero;
            return time;
        }

        [HttpGet("{walletId}/pull-payments/{pullPaymentId}/archive")]
        public IActionResult ArchivePullPayment(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId,
            string pullPaymentId)
        {
            return View("Confirm", new ConfirmModel("Archive pull payment", "Do you really want to archive the pull payment?", "Archive"));
        }
        
        [HttpPost("{walletId}/pull-payments/{pullPaymentId}/archive")]
        public async Task<IActionResult> ArchivePullPaymentPost(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId,
            string pullPaymentId)
        {
            await _pullPaymentService.Cancel(new HostedServices.PullPaymentHostedService.CancelRequest(pullPaymentId));
            this.TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Pull payment archived",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction(nameof(PullPayments), new { walletId = walletId.ToString() });
        }

        [HttpPost("{walletId}/payouts")]
        public async Task<IActionResult> PayoutsPost(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, PayoutsModel vm, CancellationToken cancellationToken)
        {
            if (vm is null || GetDerivationSchemeSettings(walletId) == null)
                return NotFound();

            var storeId = walletId.StoreId;
            var paymentMethodId = PaymentMethodId.Parse(vm.PaymentMethodId);
            var handler = _payoutHandlers
                .FirstOrDefault(handler => handler.CanHandle(paymentMethodId));
            var commandState = Enum.Parse<PayoutState>(vm.Command.Split("-").First());
            var payoutIds = vm.GetSelectedPayouts(commandState);
            if (payoutIds.Length == 0)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = "No payout selected",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(Payouts), new
                {
                    walletId = walletId.ToString(),
                    pullPaymentId = vm.PullPaymentId,
                    paymentMethodId = paymentMethodId.ToString()
                });
            }
            var command = vm.Command.Substring(vm.Command.IndexOf('-', StringComparison.InvariantCulture) + 1);
            if (handler != null)
            {
                var result = await handler.DoSpecificAction(command, payoutIds, walletId.StoreId);
                if (result != null)
                {
                    TempData.SetStatusMessageModel(result);
                }
            }
            switch (command)
            {
                case "approve-pay":
                case "approve":
                {
                    await using var ctx = this._dbContextFactory.CreateContext();
                    ctx.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                    var payouts = await GetPayoutsForPaymentMethod(paymentMethodId, ctx, payoutIds, storeId, cancellationToken);

                    var failed = false;
                    for (int i = 0; i < payouts.Count; i++)
                    {
                        var payout = payouts[i];
                        if (payout.State != PayoutState.AwaitingApproval)
                            continue;
                        var rateResult = await _pullPaymentService.GetRate(payout, null, cancellationToken);
                        if (rateResult.BidAsk == null)
                        {
                            this.TempData.SetStatusMessageModel(new StatusMessageModel()
                            {
                                Message = $"Rate unavailable: {rateResult.EvaluatedRule}",
                                Severity = StatusMessageModel.StatusSeverity.Error
                            });
                            failed = true;
                            break;
                        }
                        var approveResult = await _pullPaymentService.Approve(new HostedServices.PullPaymentHostedService.PayoutApproval()
                        {
                            PayoutId = payout.Id,
                            Revision = payout.GetBlob(_jsonSerializerSettings).Revision,
                            Rate = rateResult.BidAsk.Ask
                        });
                        if (approveResult != PullPaymentHostedService.PayoutApproval.Result.Ok)
                        {
                            TempData.SetStatusMessageModel(new StatusMessageModel()
                            {
                                Message = PullPaymentHostedService.PayoutApproval.GetErrorMessage(approveResult),
                                Severity = StatusMessageModel.StatusSeverity.Error
                            });
                            failed = true;
                            break;
                        }
                    }

                    if (failed)
                    {
                        break;
                    }
                    if (command == "approve-pay")
                    {
                        goto case "pay"; 
                    }
                    TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        Message = "Payouts approved", Severity = StatusMessageModel.StatusSeverity.Success
                    });
                    break;
                }

                case "pay":
                {
                    if (handler is { }) return await handler?.InitiatePayment(paymentMethodId, payoutIds);
                    TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        Message = "Paying via this payment method is not supported", Severity = StatusMessageModel.StatusSeverity.Error
                    });
                    break;
                }

                case "mark-paid":
                {
                    await using var ctx = this._dbContextFactory.CreateContext();
                    ctx.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                    var payouts = await GetPayoutsForPaymentMethod(paymentMethodId, ctx, payoutIds, storeId, cancellationToken);
                    for (int i = 0; i < payouts.Count; i++)
                    {
                        var payout = payouts[i];
                        if (payout.State != PayoutState.AwaitingPayment)
                            continue;
                        
                        var result = await _pullPaymentService.MarkPaid(new PayoutPaidRequest()
                        {
                            PayoutId = payout.Id
                        });
                        if (result != PayoutPaidRequest.PayoutPaidResult.Ok)
                        {
                            TempData.SetStatusMessageModel(new StatusMessageModel()
                            {
                                Message = PayoutPaidRequest.GetErrorMessage(result),
                                Severity = StatusMessageModel.StatusSeverity.Error
                            });
                            return RedirectToAction(nameof(Payouts), new
                            {
                                walletId = walletId.ToString(),
                                pullPaymentId = vm.PullPaymentId,
                                paymentMethodId = paymentMethodId.ToString()
                            });
                        }
                    }

                    TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        Message = "Payouts marked as paid", Severity = StatusMessageModel.StatusSeverity.Success
                    });
                    break;
                }

                case "cancel":
                    await _pullPaymentService.Cancel(
                        new PullPaymentHostedService.CancelRequest(payoutIds));
                    TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        Message = "Payouts archived", Severity = StatusMessageModel.StatusSeverity.Success
                    });
                    break;
            }

            return RedirectToAction(nameof(Payouts),
                new
                {
                    walletId = walletId.ToString(),
                    pullPaymentId = vm.PullPaymentId,
                    paymentMethodId = paymentMethodId.ToString()
                });
        }

        private static async Task<List<PayoutData>> GetPayoutsForPaymentMethod(PaymentMethodId paymentMethodId,
            ApplicationDbContext ctx, string[] payoutIds,
            string storeId, CancellationToken cancellationToken)
        {
            var payouts = (await ctx.Payouts
                    .Include(p => p.PullPaymentData)
                    .Include(p => p.PullPaymentData.StoreData)
                    .Where(p => payoutIds.Contains(p.Id))
                    .Where(p => p.PullPaymentData.StoreId == storeId && !p.PullPaymentData.Archived)
                    .ToListAsync(cancellationToken))
                .Where(p => p.GetPaymentMethodId() == paymentMethodId)
                .ToList();
            return payouts;
        }

        [HttpGet("{walletId}/payouts")]
        public async Task<IActionResult> Payouts(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, string pullPaymentId, string paymentMethodId, PayoutState payoutState,
            int skip = 0, int count = 50)
        {
            var vm = this.ParseListQuery(new PayoutsModel
            {
                PaymentMethodId = paymentMethodId??  new PaymentMethodId(walletId.CryptoCode, PaymentTypes.BTCLike).ToString(),
                PullPaymentId = pullPaymentId, 
                PayoutState =  payoutState,
                Skip = skip,
                Count = count
            });
            vm.Payouts = new List<PayoutsModel.PayoutModel>();
            await using var ctx = _dbContextFactory.CreateContext();
            var storeId = walletId.StoreId;
            var payoutRequest = ctx.Payouts.Where(p => p.PullPaymentData.StoreId == storeId && !p.PullPaymentData.Archived);
            if (pullPaymentId != null)
            {
                payoutRequest = payoutRequest.Where(p => p.PullPaymentDataId == vm.PullPaymentId);
                vm.PullPaymentName = (await ctx.PullPayments.FindAsync(pullPaymentId)).GetBlob().Name; 
            }
            if (vm.PaymentMethodId != null)
            {
                var pmiStr = vm.PaymentMethodId;
                payoutRequest = payoutRequest.Where(p => p.PaymentMethodId == pmiStr);
            }
            
            vm.PayoutStateCount = payoutRequest.GroupBy(data => data.State)
                .Select(e => new {e.Key, Count = e.Count()})
                .ToDictionary(arg => arg.Key, arg => arg.Count);
            foreach (PayoutState value in Enum.GetValues(typeof(PayoutState)))
            {
                if(vm.PayoutStateCount.ContainsKey(value))
                    continue;
                vm.PayoutStateCount.Add(value, 0);
            }

            vm.PayoutStateCount = vm.PayoutStateCount.OrderBy(pair => pair.Key)
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            payoutRequest = payoutRequest.Where(p => p.State == vm.PayoutState);
            vm.Total = await payoutRequest.CountAsync(); 
            payoutRequest = payoutRequest.Skip(vm.Skip).Take(vm.Count);
            
            var payouts = await payoutRequest.OrderByDescending(p => p.Date)
                                             .Select(o => new
                                             {
                                                 Payout = o,
                                                 PullPayment = o.PullPaymentData
                                             }).ToListAsync();
            foreach (var item in payouts)
            {
                var ppBlob = item.PullPayment.GetBlob();
                var payoutBlob = item.Payout.GetBlob(_jsonSerializerSettings);
                var m = new PayoutsModel.PayoutModel
                {
                    PullPaymentId = item.PullPayment.Id,
                    PullPaymentName = ppBlob.Name ?? item.PullPayment.Id,
                    Date = item.Payout.Date,
                    PayoutId = item.Payout.Id,
                    Amount = _currencyTable.DisplayFormatCurrency(payoutBlob.Amount, ppBlob.Currency),
                    Destination = payoutBlob.Destination
                };
                var handler = _payoutHandlers
                    .FirstOrDefault(handler => handler.CanHandle(item.Payout.GetPaymentMethodId()));
                var proofBlob = handler?.ParseProof(item.Payout);
                m.ProofLink = proofBlob?.Link;
                vm.Payouts.Add(m);
            }
            return View(vm);
        }
    }
}
