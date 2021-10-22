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
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PayoutData = BTCPayServer.Data.PayoutData;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
    [Route("stores/{storeId}/pull-payments")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    public class StorePullPaymentsController: Controller
    {
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly IEnumerable<IPayoutHandler> _payoutHandlers;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly PullPaymentHostedService _pullPaymentService;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;

        public StoreData CurrentStore
        {
            get
            {
                return HttpContext.GetStoreData();
            }
        }
        public StorePullPaymentsController(BTCPayNetworkProvider btcPayNetworkProvider, 
            IEnumerable<IPayoutHandler> payoutHandlers, 
            CurrencyNameTable currencyNameTable, 
            PullPaymentHostedService pullPaymentHostedService,
            ApplicationDbContextFactory dbContextFactory,
            BTCPayNetworkJsonSerializerSettings  jsonSerializerSettings)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _payoutHandlers = payoutHandlers;
            _currencyNameTable = currencyNameTable;
            _pullPaymentService = pullPaymentHostedService;
            _dbContextFactory = dbContextFactory;
            _jsonSerializerSettings = jsonSerializerSettings;
        }
        
        [HttpGet("new")]
        public IActionResult NewPullPayment(string storeId)
        {
            if (CurrentStore is  null)
                return NotFound();
            var storeMethods = CurrentStore.GetSupportedPaymentMethods(_btcPayNetworkProvider).Select(method => method.PaymentId).ToList();
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
        
        [HttpPost("new")]
        public async Task<IActionResult> NewPullPayment(string storeId, NewPullPaymentModel model)
        {
            if (CurrentStore is  null)
                return NotFound();

            var storeMethods = CurrentStore.GetSupportedPaymentMethods(_btcPayNetworkProvider).Select(method => method.PaymentId).ToList();
            var paymentMethodOptions = _payoutHandlers.GetSupportedPaymentMethods(storeMethods);
            model.PaymentMethodItems =
                paymentMethodOptions.Select(id => new SelectListItem(id.ToPrettyString(), id.ToString(), true));
            model.Name ??= string.Empty;
            model.Currency = model.Currency.ToUpperInvariant().Trim();
            if (!model.PaymentMethods.Any())
            {
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
            await _pullPaymentService.CreatePullPayment(new HostedServices.CreatePullPayment()
            {
                Name = model.Name,
                Amount = model.Amount,
                Currency = model.Currency,
                StoreId = storeId,
                PaymentMethodIds = selectedPaymentMethodIds,
                EmbeddedCSS = model.EmbeddedCSS,
                CustomCSSLink = model.CustomCSSLink
            });
            this.TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Pull payment request created",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction(nameof(PullPayments), new { storeId = storeId });
        }
        
        [HttpGet("")]
        public async Task<IActionResult> PullPayments(string storeId, int skip = 0, int count = 50, 
            string sortOrder = "desc")
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var now = DateTimeOffset.UtcNow;
            var ppsQuery = ctx.PullPayments
                .Include(data => data.Payouts)
                .Where(p => p.StoreId == storeId && !p.Archived);


            if (sortOrder != null)
            {
                switch (sortOrder)
                {
                    case "desc":
                        ViewData["NextStartSortOrder"] = "asc";
                        ppsQuery = ppsQuery.OrderByDescending(p => p.StartDate);
                        break;
                    case "asc":
                        ppsQuery = ppsQuery.OrderBy(p => p.StartDate);
                        ViewData["NextStartSortOrder"] = "desc";
                        break;
                }
            }

            var vm = this.ParseListQuery(new PullPaymentsModel()
            {
                Skip = skip, Count = count, Total = await ppsQuery.CountAsync()
            });
            var pps = (await ppsQuery
                    .Skip(vm.Skip)
                    .Take(vm.Count)
                    .ToListAsync()
                );
            foreach (var pp in pps)
            {
                var totalCompleted = pp.Payouts.Where(p => (p.State == PayoutState.Completed || 
                                                            p.State == PayoutState.InProgress) &&  p.IsInPeriod(pp, now))
                                                .Select(o => o.GetBlob(_jsonSerializerSettings).Amount).Sum();
                var totalAwaiting = pp.Payouts.Where(p => (p.State == PayoutState.AwaitingPayment ||
                                                           p.State == PayoutState.AwaitingApproval) &&
                                                          p.IsInPeriod(pp, now)).Select(o => o.GetBlob(_jsonSerializerSettings).Amount).Sum();;
                var ppBlob = pp.GetBlob();
                var ni = _currencyNameTable.GetCurrencyData(ppBlob.Currency, true);
                var nfi = _currencyNameTable.GetNumberFormatInfo(ppBlob.Currency, true);
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
                        Limit = _currencyNameTable.DisplayFormatCurrency(ppBlob.Limit, ppBlob.Currency),
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

        [HttpGet("{pullPaymentId}/archive")]
        public IActionResult ArchivePullPayment(string storeId,
            string pullPaymentId)
        {
            return View("Confirm", new ConfirmModel("Archive pull payment", "Do you really want to archive the pull payment?", "Archive"));
        }
        
        [HttpPost("{pullPaymentId}/archive")]
        public async Task<IActionResult> ArchivePullPaymentPost(string storeId,
            string pullPaymentId)
        {
            await _pullPaymentService.Cancel(new HostedServices.PullPaymentHostedService.CancelRequest(pullPaymentId));
            this.TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Pull payment archived",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction(nameof(PullPayments), new { storeId = storeId });
        }

        [HttpPost("payouts")]
        public async Task<IActionResult> PayoutsPost(
            string storeId, PayoutsModel vm, CancellationToken cancellationToken)
        {
            if (vm is null)
                return NotFound();
            var paymentMethodId = PaymentMethodId.Parse(vm.PaymentMethodId);
            var handler = _payoutHandlers
                .FindPayoutHandler(paymentMethodId);
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
                    storeId = storeId,
                    pullPaymentId = vm.PullPaymentId,
                    paymentMethodId = paymentMethodId.ToString()
                });
            }
            var command = vm.Command.Substring(vm.Command.IndexOf('-', StringComparison.InvariantCulture) + 1);
            if (handler != null)
            {
                var result = await handler.DoSpecificAction(command, payoutIds, storeId);
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
                                storeId = storeId,
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
                    storeId = storeId,
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

        [HttpGet("payouts")]
        public async Task<IActionResult> Payouts(
            string storeId, string pullPaymentId, string paymentMethodId, PayoutState payoutState,
            int skip = 0, int count = 50)
        {
            var vm = this.ParseListQuery(new PayoutsModel
            {
                PaymentMethodId = paymentMethodId?? _payoutHandlers.GetSupportedPaymentMethods().First().ToString(),
                PullPaymentId = pullPaymentId, 
                PayoutState =  payoutState,
                Skip = skip,
                Count = count
            });
            vm.Payouts = new List<PayoutsModel.PayoutModel>();
            await using var ctx = _dbContextFactory.CreateContext();
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
                    Amount = _currencyNameTable.DisplayFormatCurrency(payoutBlob.Amount, ppBlob.Currency),
                    Destination = payoutBlob.Destination
                };
                var handler = _payoutHandlers
                    .FindPayoutHandler(item.Payout.GetPaymentMethodId());
                var proofBlob = handler?.ParseProof(item.Payout);
                m.ProofLink = proofBlob?.Link;
                vm.Payouts.Add(m);
            }
            return View(vm);
        }
    }
}
