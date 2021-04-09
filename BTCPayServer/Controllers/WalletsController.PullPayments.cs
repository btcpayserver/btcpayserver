using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Views;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using PayoutData = BTCPayServer.Data.PayoutData;

namespace BTCPayServer.Controllers
{
    public partial class WalletsController
    {
        [HttpGet]
        [Route("{walletId}/pull-payments/new")]
        public IActionResult NewPullPayment([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId)
        {
            if (GetDerivationSchemeSettings(walletId) == null)
                return NotFound();

            return View(new NewPullPaymentModel
            {
                Name = "",
                Currency = "BTC",
                CustomCSSLink = "",
                EmbeddedCSS = "",
            });
        }

        [HttpPost]
        [Route("{walletId}/pull-payments/new")]
        public async Task<IActionResult> NewPullPayment([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, NewPullPaymentModel model)
        {
            if (GetDerivationSchemeSettings(walletId) == null)
                return NotFound();

            model.Name ??= string.Empty;
            model.Currency = model.Currency.ToUpperInvariant().Trim();
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
            var paymentMethodId = walletId.GetPaymentMethodId();
            var n = this.NetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            if (n is null || paymentMethodId.PaymentType != PaymentTypes.BTCLike || n.ReadonlyWallet)
                ModelState.AddModelError(nameof(model.Name), "Pull payments are not supported with this wallet");
            if (!ModelState.IsValid)
                return View(model);
            await _pullPaymentService.CreatePullPayment(new HostedServices.CreatePullPayment()
            {
                Name = model.Name,
                Amount = model.Amount,
                Currency = model.Currency,
                StoreId = walletId.StoreId,
                PaymentMethodIds = new[] { paymentMethodId },
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

        [HttpGet]
        [Route("{walletId}/pull-payments")]
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

        [HttpGet]
        [Route("{walletId}/pull-payments/{pullPaymentId}/archive")]
        public IActionResult ArchivePullPayment(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId,
            string pullPaymentId)
        {
            return View("Confirm", new ConfirmModel()
            {
                Title = "Archive the pull payment",
                Description = "Do you really want to archive this pull payment?",
                ButtonClass = "btn-danger",
                Action = "Archive"
            });
        }
        [HttpPost]
        [Route("{walletId}/pull-payments/{pullPaymentId}/archive")]
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

        [HttpPost]
        [Route("{walletId}/payouts")]
        public async Task<IActionResult> PayoutsPost(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, PayoutsModel vm, CancellationToken cancellationToken)
        {
            if (vm is null || GetDerivationSchemeSettings(walletId) == null)
                return NotFound();

            var storeId = walletId.StoreId;
            var paymentMethodId = new PaymentMethodId(walletId.CryptoCode, PaymentTypes.BTCLike);

            var commandState = Enum.Parse<PayoutState>(vm.Command.Split("-").First());
            var payoutIds = vm.GetSelectedPayouts(commandState);
            if (payoutIds.Length == 0)
            {
                this.TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = "No payout selected",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(Payouts), new
                {
                    walletId = walletId.ToString(),
                    pullPaymentId = vm.PullPaymentId
                });
            }

            var command = vm.Command.Substring(vm.Command.IndexOf('-', StringComparison.InvariantCulture) + 1);

            switch (command)
            {
                
                case "approve-pay":
                case "approve":
                {
                    await using var ctx = this._dbContextFactory.CreateContext();
                    ctx.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                    var payouts = await GetPayoutsForPaymentMethod(walletId.GetPaymentMethodId(), ctx, payoutIds, storeId, cancellationToken);

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
                            return RedirectToAction(nameof(Payouts), new
                            {
                                walletId = walletId.ToString(),
                                pullPaymentId = vm.PullPaymentId
                            });
                        }
                        var approveResult = await _pullPaymentService.Approve(new HostedServices.PullPaymentHostedService.PayoutApproval()
                        {
                            PayoutId = payout.Id,
                            Revision = payout.GetBlob(_jsonSerializerSettings).Revision,
                            Rate = rateResult.BidAsk.Ask
                        });
                        if (approveResult != HostedServices.PullPaymentHostedService.PayoutApproval.Result.Ok)
                        {
                            this.TempData.SetStatusMessageModel(new StatusMessageModel()
                            {
                                Message = PullPaymentHostedService.PayoutApproval.GetErrorMessage(approveResult),
                                Severity = StatusMessageModel.StatusSeverity.Error
                            });
                            return RedirectToAction(nameof(Payouts), new
                            {
                                walletId = walletId.ToString(),
                                pullPaymentId = vm.PullPaymentId
                            });
                        }
                    }

                    if (command == "approve-pay")
                    {
                        goto case "pay"; 
                    }

                    TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        Message = "Payouts approved", Severity = StatusMessageModel.StatusSeverity.Success
                    });
                    return RedirectToAction(nameof(Payouts),
                        new {walletId = walletId.ToString(), pullPaymentId = vm.PullPaymentId});
                }

                case "pay":
                {
                    await using var ctx = this._dbContextFactory.CreateContext();
                    ctx.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                    var payouts = await GetPayoutsForPaymentMethod(walletId.GetPaymentMethodId(), ctx, payoutIds, storeId, cancellationToken);

                    var walletSend = (WalletSendModel)((ViewResult)(await this.WalletSend(walletId))).Model;
                    walletSend.Outputs.Clear();
                    var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
                    List<string> bip21 = new List<string>(); 
                    foreach (var payout in payouts)
                    {
                        var blob = payout.GetBlob(_jsonSerializerSettings);
                        if (payout.GetPaymentMethodId() != paymentMethodId)
                            continue;
                        bip21.Add(network.GenerateBIP21(payout.Destination, new Money(blob.CryptoAmount.Value, MoneyUnit.BTC)));
                        
                    }

                    return RedirectToAction(nameof(WalletSend), new {walletId, bip21});
                }

                case "cancel":
                    await _pullPaymentService.Cancel(
                        new HostedServices.PullPaymentHostedService.CancelRequest(payoutIds));
                    this.TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        Message = "Payouts archived", Severity = StatusMessageModel.StatusSeverity.Success
                    });
                    return RedirectToAction(nameof(Payouts),
                        new {walletId = walletId.ToString(), pullPaymentId = vm.PullPaymentId});
            }

            return NotFound();
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

        [HttpGet]
        [Route("{walletId}/payouts")]
        public async Task<IActionResult> Payouts(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, PayoutsModel vm = null)
        {
            vm ??= new PayoutsModel();
            vm.PayoutStateSets ??= ((PayoutState[]) Enum.GetValues(typeof(PayoutState))).Select(state =>
                new PayoutsModel.PayoutStateSet() {State = state, Payouts = new List<PayoutsModel.PayoutModel>()}).ToList();
            using var ctx = this._dbContextFactory.CreateContext();
            var storeId = walletId.StoreId;
            vm.PaymentMethodId = new PaymentMethodId(walletId.CryptoCode, PaymentTypes.BTCLike);
            var payoutRequest = ctx.Payouts.Where(p => p.PullPaymentData.StoreId == storeId && !p.PullPaymentData.Archived);
            if (vm.PullPaymentId != null)
            {
                payoutRequest = payoutRequest.Where(p => p.PullPaymentDataId == vm.PullPaymentId);
            }
            var payouts = await payoutRequest.OrderByDescending(p => p.Date)
                                             .Select(o => new
                                             {
                                                 Payout = o,
                                                 PullPayment = o.PullPaymentData
                                             }).ToListAsync();
            foreach (var stateSet in payouts.GroupBy(arg => arg.Payout.State))
            {
                var state = vm.PayoutStateSets.SingleOrDefault(set => set.State == stateSet.Key);
                if (state == null)
                {
                    state = new PayoutsModel.PayoutStateSet()
                    {
                        Payouts = new List<PayoutsModel.PayoutModel>(), State = stateSet.Key
                    };
                    vm.PayoutStateSets.Add(state);
                }

                foreach (var item in stateSet)
                {

                    if (item.Payout.GetPaymentMethodId() != vm.PaymentMethodId)
                        continue;
                    var ppBlob = item.PullPayment.GetBlob();
                    var payoutBlob = item.Payout.GetBlob(_jsonSerializerSettings);
                    var m = new PayoutsModel.PayoutModel();
                    m.PullPaymentId = item.PullPayment.Id;
                    m.PullPaymentName = ppBlob.Name ?? item.PullPayment.Id;
                    m.Date = item.Payout.Date;
                    m.PayoutId = item.Payout.Id;
                    m.Amount = _currencyTable.DisplayFormatCurrency(payoutBlob.Amount, ppBlob.Currency);
                    m.Destination = payoutBlob.Destination;
                    var handler = _payoutHandlers
                        .FirstOrDefault(handler => handler.CanHandle(item.Payout.GetPaymentMethodId()));
                    var proofBlob = handler?.ParseProof(item.Payout);
                    m.TransactionLink = proofBlob?.Link;
                    state.Payouts.Add(m);

                }
            }

            vm.PayoutStateSets = vm.PayoutStateSets.Where(set => set.Payouts?.Any() is true).ToList();
            return View(vm);
        }
    }
}
