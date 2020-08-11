using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime.Internal.Transform;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Views;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace BTCPayServer.Controllers
{
    public class PullPaymentController : Controller
    {
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly PullPaymentHostedService _pullPaymentHostedService;
        private readonly BTCPayNetworkJsonSerializerSettings _serializerSettings;
        private readonly IEnumerable<IPayoutHandler> _payoutHandlers;
        private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        public PullPaymentController(ApplicationDbContextFactory dbContextFactory,
            CurrencyNameTable currencyNameTable,
            PullPaymentHostedService pullPaymentHostedService,
            BTCPayNetworkJsonSerializerSettings serializerSettings,
            IEnumerable<IPayoutHandler> payoutHandlers,
            BTCPayNetworkJsonSerializerSettings jsonSerializerSettings,
            BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _dbContextFactory = dbContextFactory;
            _currencyNameTable = currencyNameTable;
            _pullPaymentHostedService = pullPaymentHostedService;
            _serializerSettings = serializerSettings;
            _payoutHandlers = payoutHandlers;
            _jsonSerializerSettings = jsonSerializerSettings;
            _btcPayNetworkProvider = btcPayNetworkProvider;
        }

        [AllowAnonymous]
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
                    ProofBlob = _payoutHandlers.FirstOrDefault(handler => handler.CanHandle(o.GetPaymentMethodId()))
                        ?.ParseProof(o)
                });
            var cd = _currencyNameTable.GetCurrencyData(blob.Currency, false);
            var totalPaid = payouts.Where(p => p.Entity.State != PayoutState.Cancelled).Select(p => p.Blob.Amount)
                .Sum();
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
                LastUpdated = DateTime.Now,
                Payouts = payouts
                    .Select(entity => new ViewPullPaymentModel.PayoutLine()
                    {
                        Id = entity.Entity.Id,
                        Amount = entity.Blob.Amount,
                        AmountFormatted = _currencyNameTable.FormatCurrency(entity.Blob.Amount, blob.Currency),
                        Currency = blob.Currency,
                        Status = entity.Entity.State.ToString(),
                        Destination = entity.Blob.Destination,
                        Link = entity.ProofBlob?.Link,
                        TransactionId = entity.ProofBlob?.Id
                    }).ToList()
            };
            vm.IsPending &= vm.AmountDue > 0.0m;
            return View(nameof(ViewPullPayment), vm);
        }

        [AllowAnonymous]
        [Route("pull-payments/{pullPaymentId}/claim")]
        [HttpPost]
        public async Task<IActionResult> ClaimPullPayment(string pullPaymentId, ViewPullPaymentModel vm)
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp is null)
            {
                ModelState.AddModelError(nameof(pullPaymentId), "This pull payment does not exists");
            }
        
            var ppBlob = pp.GetBlob();
            var paymentMethodId = ppBlob.SupportedPaymentMethods.FirstOrDefault(id => string.IsNullOrEmpty( vm.PaymentMethod) || id.ToString().Equals(vm.PaymentMethod, StringComparison.InvariantCultureIgnoreCase));
            if (paymentMethodId is null)
            {
                ModelState.AddModelError(nameof(vm.PaymentMethod), "Payment method not available");
            }
            var payoutHandler = _payoutHandlers.FirstOrDefault(handler => handler.CanHandle(paymentMethodId));
            IClaimDestination destination = await payoutHandler?.ParseClaimDestination(paymentMethodId, vm.Destination);
            if (destination is null)
            {
                ModelState.AddModelError(nameof(vm.Destination), $"Invalid destination");
            }

            if (!ModelState.IsValid)
            {
                return await ViewPullPayment(pullPaymentId);
            }

            var result = await _pullPaymentHostedService.Claim(new ClaimRequest()
            {
                Destination = destination,
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
                    Message =
                        $"You posted a claim of {_currencyNameTable.DisplayFormatCurrency(vm.ClaimedAmount, ppBlob.Currency)} to {vm.Destination}, this will get fullfilled later.",
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
            }

            return RedirectToAction(nameof(ViewPullPayment), new {pullPaymentId = pullPaymentId});
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [AutoValidateAntiforgeryToken]
        [HttpGet]
        [Route("stores/{storeId}/pull-payments/new")]
        public IActionResult NewPullPayment(
            string storeId)
        {
            var store = this.HttpContext.GetStoreData();
            return View(new NewPullPaymentModel() {Name = "", Currency = "BTC", AvailablePaymentMethods = GetAvailablePaymentMethods(store)});
        }

        private SelectList GetAvailablePaymentMethods(StoreData store)
        {
            var enabledMethods = store.GetEnabledPaymentIds(_btcPayNetworkProvider)
                .Where(id => _payoutHandlers.Any(handler => handler.CanHandle(id)));
            return new SelectList(enabledMethods.Select(id => new SelectListItem(id.ToPrettyString(), id.ToString())),
                nameof(SelectListItem.Value), nameof(SelectListItem.Text));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [AutoValidateAntiforgeryToken]
        [HttpPost]
        [Route("stores/{storeId}/pull-payments/new")]
        public async Task<IActionResult> NewPullPayment(
            string storeId, NewPullPaymentModel model)
        {
            
            var store = this.HttpContext.GetStoreData();
            model.AvailablePaymentMethods = GetAvailablePaymentMethods(store);
            model.Name ??= string.Empty;
            model.Currency = model.Currency.ToUpperInvariant().Trim();
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

            var x = model.AvailablePaymentMethods.Items.OfType<SelectListItem>().Select(item => item.Value).ToArray();
            if (model.PaymentMethods.Any(s => !x.Contains(s)))
            {
                ModelState.AddModelError(nameof(model.Name), "Unsupported payment method specified");
            }

            if (!ModelState.IsValid)
                return View(model);
            await _pullPaymentHostedService.CreatePullPayment(new HostedServices.CreatePullPayment()
            {
                Name = model.Name,
                Amount = model.Amount,
                Currency = model.Currency,
                StoreId = storeId,
                PaymentMethodIds = model.PaymentMethods.Select(PaymentMethodId.Parse).ToArray()
            });
            this.TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Pull payment request created", Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction(nameof(PullPayments), new { storeId});
        }


        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [AutoValidateAntiforgeryToken]
        [HttpGet]
        [Route("stores/{storeId}/pull-payments")]
        public async Task<IActionResult> PullPayments(
            string storeId)
        {
            await using var ctx = this._dbContextFactory.CreateContext();
            var now = DateTimeOffset.UtcNow;
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
                        ResetIn =
                            period?.End is DateTimeOffset nr ? ZeroIfNegative(nr - now).TimeString() : null,
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


        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [AutoValidateAntiforgeryToken]
        [HttpGet]
        [Route("stores/{storeId}/pull-payments/{pullPaymentId}/archive")]
        public IActionResult ArchivePullPayment(
            string storeId,
            string pullPaymentId)
        {
            return View("Confirm",
                new ConfirmModel()
                {
                    Title = "Archive the pull payment",
                    Description = "Do you really want to archive this pull payment?",
                    ButtonClass = "btn-danger",
                    Action = "Archive"
                });
        }


        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [AutoValidateAntiforgeryToken]
        [HttpPost]
        [Route("stores/{storeId}/pull-payments/{pullPaymentId}/archive")]
        public async Task<IActionResult> ArchivePullPaymentPost(
            string storeId,
            string pullPaymentId)
        {
            await _pullPaymentHostedService.Cancel(new HostedServices.PullPaymentHostedService.CancelRequest(pullPaymentId));
            this.TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Pull payment archived", Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction(nameof(PullPayments), new {storeId});
        }
        
         [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [AutoValidateAntiforgeryToken]
        [HttpPost]
        [Route("stores/{storeId}/payouts/{paymentMethodId}")]
        public async Task<IActionResult> PayoutsPost(
            string storeId, PayoutsModel vm, CancellationToken cancellationToken)
        {
            if (vm is null)
                return NotFound();
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
                    storeId,
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
                    .ToList();

                for (int i = 0; i < payouts.Count; i++)
                {
                    var payout = payouts[i];
                    if (payout.State != PayoutState.AwaitingApproval)
                        continue;
                    var rateResult = await _pullPaymentHostedService.GetRate(payout, null, cancellationToken);
                    if (rateResult.BidAsk == null)
                    {
                        this.TempData.SetStatusMessageModel(new StatusMessageModel()
                        {
                            Message = $"Rate unavailable: {rateResult.EvaluatedRule}",
                            Severity = StatusMessageModel.StatusSeverity.Error
                        });
                        return RedirectToAction(nameof(Payouts), new
                        {
                            storeId,
                            pullPaymentId = vm.PullPaymentId
                        });
                    }
                    var approveResult = await _pullPaymentHostedService.Approve(new HostedServices.PullPaymentHostedService.PayoutApproval()
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
                            storeId,
                            pullPaymentId = vm.PullPaymentId
                        });
                    }
                    payouts[i] = await ctx.Payouts.FindAsync(payouts[i].Id);
                }
                return await _payoutHandlers.FirstOrDefault(handler => handler.CanHandle(payouts.First().GetPaymentMethodId()))?.CreatePayout(this, payouts);
            }
            else if (vm.Command == "cancel")
            {
                await _pullPaymentHostedService.Cancel(new HostedServices.PullPaymentHostedService.CancelRequest(payoutIds));
                this.TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = "Payouts archived",
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
                return RedirectToAction(nameof(Payouts), new
                {
                    storeId,
                    pullPaymentId = vm.PullPaymentId
                });
            }
            else
            {
                return NotFound();
            }
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [AutoValidateAntiforgeryToken]
        [HttpGet]
        [Route("stores/{storeId}/payouts")]
        public async Task<IActionResult> Payouts(
            string storeId, string pullPaymentId)
        {
            using var ctx = this._dbContextFactory.CreateContext();
            var payoutRequest = ctx.Payouts.Where(p => p.PullPaymentData.StoreId == storeId && !p.PullPaymentData.Archived);
            if (pullPaymentId != null)
            {
                payoutRequest = payoutRequest.Where(p => p.PullPaymentDataId == pullPaymentId);
            }
            var payouts = await payoutRequest.OrderByDescending(p => p.Date)
                                             .Select(o => new
                                             {
                                                 Payout = o,
                                                 PullPayment = o.PullPaymentData
                                             }).ToListAsync();
var result = new Dictionary<string, PayoutsModel>();
            foreach (var group in payouts.GroupBy(arg => arg.Payout.PaymentMethodId))
            {
                
                var vm = new PayoutsModel();
                vm.WaitingForApproval = new List<PayoutsModel.PayoutModel>();
                vm.Other = new List<PayoutsModel.PayoutModel>();
                foreach (var item in group)
                {

                    var ppBlob = item.PullPayment.GetBlob();
                    var payoutBlob = item.Payout.GetBlob(_jsonSerializerSettings);
                    var m = new PayoutsModel.PayoutModel();
                    m.PullPaymentId = item.PullPayment.Id;
                    m.PullPaymentName = ppBlob.Name ?? item.PullPayment.Id;
                    m.Date = item.Payout.Date;
                    m.PayoutId = item.Payout.Id;
                    m.Amount = _currencyNameTable.DisplayFormatCurrency(payoutBlob.Amount, ppBlob.Currency);
                    m.Destination = payoutBlob.Destination;
                    if (item.Payout.State == PayoutState.AwaitingPayment ||
                        item.Payout.State == PayoutState.AwaitingApproval)
                    {
                        vm.WaitingForApproval.Add(m);
                    }
                    else
                    {

                        var proofBlob = _payoutHandlers
                            .FirstOrDefault(handler => handler.CanHandle(item.Payout.GetPaymentMethodId()))
                            ?.ParseProof(item.Payout);
                        m.TransactionLink = proofBlob?.Link;
                        vm.Other.Add(m);
                    }

                }
                result.Add(group.Key, vm);
            }
            return View("PayoutsGroup",result);
        }
        
    }
}
