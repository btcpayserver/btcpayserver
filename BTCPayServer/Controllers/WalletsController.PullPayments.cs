using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

namespace BTCPayServer.Controllers
{
    public partial class WalletsController
    {
        [HttpGet]
        [Route("{walletId}/pull-payments/new")]
        public IActionResult NewPullPayment([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId)
        {
            return View(new NewPullPaymentModel()
            {
                Name = "",
                Currency = "BTC"
            });
        }

        [HttpPost]
        [Route("{walletId}/pull-payments/new")]
        public async Task<IActionResult> NewPullPayment([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, NewPullPaymentModel model)
        {
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
                PaymentMethodIds = new[] { paymentMethodId }
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
            var vm = new PullPaymentsModel();
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
            if (vm is null)
                return NotFound();
            var storeId = walletId.StoreId;
            var paymentMethodId = new PaymentMethodId(walletId.CryptoCode, PaymentTypes.BTCLike);
            var payoutIds = vm.WaitingForApproval.Where(p => p.Selected).Select(p => p.PayoutId).ToArray();
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
            if (vm.Command == "pay")
            {
                using var ctx = this._dbContextFactory.CreateContext();
                ctx.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                var payouts = (await ctx.Payouts
                    .Include(p => p.PullPaymentData)
                    .Include(p => p.PullPaymentData.StoreData)
                    .Where(p => payoutIds.Contains(p.Id))
                    .Where(p => p.PullPaymentData.StoreId == storeId && !p.PullPaymentData.Archived)
                    .ToListAsync())
                    .Where(p => p.GetPaymentMethodId() == walletId.GetPaymentMethodId())
                    .ToList();

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
                    payouts[i] = await ctx.Payouts.FindAsync(payouts[i].Id);
                }
                var walletSend = (WalletSendModel)((ViewResult)(await this.WalletSend(walletId))).Model;
                walletSend.Outputs.Clear();
                foreach (var payout in payouts)
                {
                    var blob = payout.GetBlob(_jsonSerializerSettings);
                    if (payout.GetPaymentMethodId() != paymentMethodId)
                        continue;
                    var output = new WalletSendModel.TransactionOutput()
                    {
                        Amount = blob.CryptoAmount,
                        DestinationAddress = blob.Destination.Address.ToString()
                    };
                    walletSend.Outputs.Add(output);
                }
                return View(nameof(walletSend), walletSend);
            }
            else if (vm.Command == "cancel")
            {
                await _pullPaymentService.Cancel(new HostedServices.PullPaymentHostedService.CancelRequest(payoutIds));
                this.TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = "Payouts archived",
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
                return RedirectToAction(nameof(Payouts), new
                {
                    walletId = walletId.ToString(),
                    pullPaymentId = vm.PullPaymentId
                });
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet]
        [Route("{walletId}/payouts")]
        public async Task<IActionResult> Payouts(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, PayoutsModel vm = null)
        {
            vm ??= new PayoutsModel();
            using var ctx = this._dbContextFactory.CreateContext();
            var storeId = walletId.StoreId;
            var paymentMethodId = new PaymentMethodId(walletId.CryptoCode, PaymentTypes.BTCLike);
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
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            vm.WaitingForApproval = new List<PayoutsModel.PayoutModel>();
            vm.Other = new List<PayoutsModel.PayoutModel>();
            foreach (var item in payouts)
            {
                if (item.Payout.GetPaymentMethodId() != paymentMethodId)
                    continue;
                var ppBlob = item.PullPayment.GetBlob();
                var payoutBlob = item.Payout.GetBlob(_jsonSerializerSettings);
                var m = new PayoutsModel.PayoutModel();
                m.PullPaymentId = item.PullPayment.Id;
                m.PullPaymentName = ppBlob.Name ?? item.PullPayment.Id;
                m.Date = item.Payout.Date;
                m.PayoutId = item.Payout.Id;
                m.Amount = _currencyTable.DisplayFormatCurrency(payoutBlob.Amount, ppBlob.Currency);
                m.Destination = payoutBlob.Destination.Address.ToString();
                if (item.Payout.State == PayoutState.AwaitingPayment || item.Payout.State == PayoutState.AwaitingApproval)
                {
                    vm.WaitingForApproval.Add(m);
                }
                else
                {
                    if (item.Payout.GetPaymentMethodId().PaymentType == PaymentTypes.BTCLike &&
                        item.Payout.GetProofBlob(this._jsonSerializerSettings)?.TransactionId is uint256 txId)
                        m.TransactionLink = string.Format(CultureInfo.InvariantCulture, network.BlockExplorerLink, txId);
                    vm.Other.Add(m);
                }
            }
            return View(vm);
        }
    }
}
