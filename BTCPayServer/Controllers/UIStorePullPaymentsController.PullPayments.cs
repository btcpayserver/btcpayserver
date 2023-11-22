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
using Newtonsoft.Json.Linq;
using MarkPayoutRequest = BTCPayServer.HostedServices.MarkPayoutRequest;
using PayoutData = BTCPayServer.Data.PayoutData;
using PullPaymentData = BTCPayServer.Data.PullPaymentData;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
    [Authorize(Policy = Policies.CanViewPullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    public class UIStorePullPaymentsController : Controller
    {
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly IEnumerable<IPayoutHandler> _payoutHandlers;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly DisplayFormatter _displayFormatter;
        private readonly PullPaymentHostedService _pullPaymentService;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;
        private readonly IAuthorizationService _authorizationService;

        public StoreData CurrentStore
        {
            get
            {
                return HttpContext.GetStoreData();
            }
        }

        public UIStorePullPaymentsController(BTCPayNetworkProvider btcPayNetworkProvider,
            IEnumerable<IPayoutHandler> payoutHandlers,
            CurrencyNameTable currencyNameTable,
            DisplayFormatter displayFormatter,
            PullPaymentHostedService pullPaymentHostedService,
            ApplicationDbContextFactory dbContextFactory,
            BTCPayNetworkJsonSerializerSettings jsonSerializerSettings,
            IAuthorizationService authorizationService)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _payoutHandlers = payoutHandlers;
            _currencyNameTable = currencyNameTable;
            _displayFormatter = displayFormatter;
            _pullPaymentService = pullPaymentHostedService;
            _dbContextFactory = dbContextFactory;
            _jsonSerializerSettings = jsonSerializerSettings;
            _authorizationService = authorizationService;
        }

        [HttpGet("stores/{storeId}/pull-payments/new")]
        [Authorize(Policy = Policies.CanCreateNonApprovedPullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> NewPullPayment(string storeId)
        {
            if (CurrentStore is null)
                return NotFound();

            var paymentMethods = await _payoutHandlers.GetSupportedPaymentMethods(CurrentStore);
            if (!paymentMethods.Any())
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = "You must enable at least one payment method before creating a pull payment.",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(UIStoresController.Dashboard), "UIStores", new { storeId });
            }

            return View(new NewPullPaymentModel
            {
                Name = "",
                Currency = CurrentStore.GetStoreBlob().DefaultCurrency,
                CustomCSSLink = "",
                EmbeddedCSS = "",
                PaymentMethodItems =
                    paymentMethods.Select(id => new SelectListItem(id.ToPrettyString(), id.ToString(), true))
            });
        }

        [HttpPost("stores/{storeId}/pull-payments/new")]
        [Authorize(Policy = Policies.CanCreateNonApprovedPullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> NewPullPayment(string storeId, NewPullPaymentModel model)
        {
            if (CurrentStore is null)
                return NotFound();

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
            if (model.AutoApproveClaims)
            {
                model.AutoApproveClaims = (await
                    _authorizationService.AuthorizeAsync(User, storeId, Policies.CanCreatePullPayments)).Succeeded;
            }
            await _pullPaymentService.CreatePullPayment(new HostedServices.CreatePullPayment()
            {
                Name = model.Name,
                Description = model.Description,
                Amount = model.Amount,
                Currency = model.Currency,
                StoreId = storeId,
                PaymentMethodIds = selectedPaymentMethodIds,
                EmbeddedCSS = model.EmbeddedCSS,
                CustomCSSLink = model.CustomCSSLink,
                BOLT11Expiration = TimeSpan.FromDays(model.BOLT11Expiration),
                AutoApproveClaims = model.AutoApproveClaims
            });
            this.TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Pull payment request created",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction(nameof(PullPayments), new { storeId = storeId });
        }

        [HttpGet("stores/{storeId}/pull-payments")]
        public async Task<IActionResult> PullPayments(
            string storeId,
            PullPaymentState pullPaymentState,
            int skip = 0,
            int count = 50,
            string sortOrder = "desc"
        )
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var now = DateTimeOffset.UtcNow;
            var ppsQuery = ctx.PullPayments
                .Include(data => data.Payouts)
                .Where(p => p.StoreId == storeId);

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

            var paymentMethods = await _payoutHandlers.GetSupportedPaymentMethods(HttpContext.GetStoreData());
            if (!paymentMethods.Any())
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = "You must enable at least one payment method before creating a pull payment.",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(UIStoresController.Dashboard), "UIStores", new { storeId });
            }

            var vm = this.ParseListQuery(new PullPaymentsModel
            {
                Skip = skip,
                Count = count,
                ActiveState = pullPaymentState
            });

            switch (pullPaymentState)
            {
                case PullPaymentState.Active:
                    ppsQuery = ppsQuery
                        .Where(
                            p => !p.Archived &&
                                 (p.EndDate != null ? p.EndDate > DateTimeOffset.UtcNow : true) &&
                                 p.StartDate <= DateTimeOffset.UtcNow
                        );
                    break;
                case PullPaymentState.Archived:
                    ppsQuery = ppsQuery.Where(p => p.Archived);
                    break;
                case PullPaymentState.Expired:
                    ppsQuery = ppsQuery.Where(p => DateTimeOffset.UtcNow > p.EndDate);
                    break;
                case PullPaymentState.Future:
                    ppsQuery = ppsQuery.Where(p => p.StartDate > DateTimeOffset.UtcNow);
                    break;
            }

            var pps = await ppsQuery
                .Skip(vm.Skip)
                .Take(vm.Count)
                .ToListAsync();
            vm.PullPayments.AddRange(pps.Select(pp =>
            {
                var blob = pp.GetBlob();
                return new PullPaymentsModel.PullPaymentModel()
                {
                    StartDate = pp.StartDate,
                    EndDate = pp.EndDate,
                    Id = pp.Id,
                    Name = blob.Name,
                    AutoApproveClaims = blob.AutoApproveClaims,
                    Progress = _pullPaymentService.CalculatePullPaymentProgress(pp, now),
                    Archived = pp.Archived
                };
            }));

            return View(vm);
        }

        [HttpGet("stores/{storeId}/pull-payments/{pullPaymentId}/archive")]
        [Authorize(Policy = Policies.CanArchivePullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public IActionResult ArchivePullPayment(string storeId,
            string pullPaymentId)
        {
            return View("Confirm",
                new ConfirmModel("Archive pull payment", "Do you really want to archive the pull payment?", "Archive"));
        }

        [HttpPost("stores/{storeId}/pull-payments/{pullPaymentId}/archive")]
        [Authorize(Policy = Policies.CanArchivePullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> ArchivePullPaymentPost(string storeId,
            string pullPaymentId)
        {
            await _pullPaymentService.Cancel(new PullPaymentHostedService.CancelRequest(pullPaymentId));
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Pull payment archived",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction(nameof(PullPayments), new { storeId });
        }

        [Authorize(Policy = Policies.CanManagePayouts, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpPost("stores/{storeId}/pull-payments/payouts")]
        [HttpPost("stores/{storeId}/pull-payments/{pullPaymentId}/payouts")]
        [HttpPost("stores/{storeId}/payouts")]
        public async Task<IActionResult> PayoutsPost(
            string storeId, PayoutsModel vm, CancellationToken cancellationToken)
        {
            if (vm is null)
                return NotFound();

            vm.PaymentMethods = await _payoutHandlers.GetSupportedPaymentMethods(HttpContext.GetStoreData());
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
                return RedirectToAction(nameof(Payouts),
                    new
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
                        var payouts =
                            await GetPayoutsForPaymentMethod(paymentMethodId, ctx, payoutIds, storeId, cancellationToken);

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

                            var approveResult = await _pullPaymentService.Approve(
                                new HostedServices.PullPaymentHostedService.PayoutApproval()
                                {
                                    PayoutId = payout.Id,
                                    Revision = payout.GetBlob(_jsonSerializerSettings).Revision,
                                    Rate = rateResult.BidAsk.Ask
                                });
                            if (approveResult.Result != PullPaymentHostedService.PayoutApproval.Result.Ok)
                            {
                                TempData.SetStatusMessageModel(new StatusMessageModel()
                                {
                                    Message = PullPaymentHostedService.PayoutApproval.GetErrorMessage(approveResult.Result),
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
                            Message = "Payouts approved",
                            Severity = StatusMessageModel.StatusSeverity.Success
                        });
                        break;
                    }

                case "pay":
                    {
                        if (handler is { })
                            return await handler?.InitiatePayment(paymentMethodId, payoutIds);
                        TempData.SetStatusMessageModel(new StatusMessageModel()
                        {
                            Message = "Paying via this payment method is not supported",
                            Severity = StatusMessageModel.StatusSeverity.Error
                        });
                        break;
                    }

                case "mark-paid":
                    {
                        await using var ctx = this._dbContextFactory.CreateContext();
                        ctx.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                        var payouts =
                            await GetPayoutsForPaymentMethod(paymentMethodId, ctx, payoutIds, storeId, cancellationToken);
                        for (int i = 0; i < payouts.Count; i++)
                        {
                            var payout = payouts[i];
                            if (payout.State != PayoutState.AwaitingPayment)
                                continue;

                            var result =
                                await _pullPaymentService.MarkPaid(new MarkPayoutRequest() { PayoutId = payout.Id });
                            if (result != MarkPayoutRequest.PayoutPaidResult.Ok)
                            {
                                TempData.SetStatusMessageModel(new StatusMessageModel()
                                {
                                    Message = MarkPayoutRequest.GetErrorMessage(result),
                                    Severity = StatusMessageModel.StatusSeverity.Error
                                });
                                return RedirectToAction(nameof(Payouts),
                                    new
                                    {
                                        storeId = storeId,
                                        pullPaymentId = vm.PullPaymentId,
                                        paymentMethodId = paymentMethodId.ToString()
                                    });
                            }
                        }

                        TempData.SetStatusMessageModel(new StatusMessageModel()
                        {
                            Message = "Payouts marked as paid",
                            Severity = StatusMessageModel.StatusSeverity.Success
                        });
                        break;
                    }

                case "cancel":
                    await _pullPaymentService.Cancel(
                        new PullPaymentHostedService.CancelRequest(payoutIds, new[] { storeId }));
                    TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        Message = "Payouts archived",
                        Severity = StatusMessageModel.StatusSeverity.Success
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
            return await PullPaymentHostedService.GetPayouts(new PullPaymentHostedService.PayoutQuery()
            {
                IncludeArchived = false,
                IncludeStoreData = true,
                Stores = new[] { storeId },
                PayoutIds = payoutIds,
                PaymentMethods = new[] { paymentMethodId.ToString() }
            }, ctx, cancellationToken);
        }

        [HttpGet("stores/{storeId}/pull-payments/{pullPaymentId}/payouts")]
        [HttpGet("stores/{storeId}/payouts")]
        [Authorize(Policy = Policies.CanViewPayouts, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> Payouts(
            string storeId, string pullPaymentId, string paymentMethodId, PayoutState payoutState,
            int skip = 0, int count = 50)
        {
            var paymentMethods = await _payoutHandlers.GetSupportedPaymentMethods(HttpContext.GetStoreData());
            if (!paymentMethods.Any())
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = "You must enable at least one payment method before creating a payout.",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(UIStoresController.Dashboard), "UIStores", new { storeId });
            }

            var vm = this.ParseListQuery(new PayoutsModel
            {
                PaymentMethods = paymentMethods,
                PaymentMethodId = paymentMethodId ?? paymentMethods.First().ToString(),
                PullPaymentId = pullPaymentId,
                PayoutState = payoutState,
                Skip = skip,
                Count = count
            });
            vm.Payouts = new List<PayoutsModel.PayoutModel>();
            await using var ctx = _dbContextFactory.CreateContext();
            var payoutRequest =
                ctx.Payouts.Where(p => p.StoreDataId == storeId && (p.PullPaymentDataId == null || !p.PullPaymentData.Archived));
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

            vm.PaymentMethodCount = (await payoutRequest.GroupBy(data => data.PaymentMethodId)
                    .Select(datas => new { datas.Key, Count = datas.Count() }).ToListAsync())
                .ToDictionary(datas => datas.Key, arg => arg.Count);
            vm.PayoutStateCount = payoutRequest.GroupBy(data => data.State)
                .Select(e => new { e.Key, Count = e.Count() })
                .ToDictionary(arg => arg.Key, arg => arg.Count);
            foreach (PayoutState value in Enum.GetValues(typeof(PayoutState)))
            {
                if (vm.PayoutStateCount.ContainsKey(value))
                    continue;
                vm.PayoutStateCount.Add(value, 0);
            }

            vm.PayoutStateCount = vm.PayoutStateCount.OrderBy(pair => pair.Key)
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            payoutRequest = payoutRequest.Where(p => p.State == vm.PayoutState);
            payoutRequest = payoutRequest.Skip(vm.Skip).Take(vm.Count);

            var payouts = await payoutRequest.OrderByDescending(p => p.Date)
                .Select(o => new { Payout = o, PullPayment = o.PullPaymentData }).ToListAsync();

            foreach (var item in payouts)
            {
                var ppBlob = item.PullPayment?.GetBlob();
                var payoutBlob = item.Payout.GetBlob(_jsonSerializerSettings);
                item.Payout.PullPaymentData = item.PullPayment;
                string payoutSource = item.Payout.GetPayoutSource(_jsonSerializerSettings);
                if (payoutBlob.Metadata?.TryGetValue("source", StringComparison.InvariantCultureIgnoreCase,
                        out var source) is true)
                {
                    payoutSource = source.Value<string>();
                }
                else
                {
                    payoutSource = ppBlob?.Name ?? item.PullPayment?.Id;
                }

                string payoutSourceLink = null;
                if (payoutBlob.Metadata?.TryGetValue("sourceLink", StringComparison.InvariantCultureIgnoreCase,
                        out var sourceLink) is true)
                {
                    payoutSourceLink = sourceLink.Value<string>();
                }
                else if(item.PullPayment?.Id is not null)
                {
                    payoutSourceLink = Url.Action("ViewPullPayment", "UIPullPayment", new { pullPaymentId = item.PullPayment?.Id });
                }
                var m = new PayoutsModel.PayoutModel
                {
                    PullPaymentId = item.PullPayment?.Id,
                    Source = payoutSource,
                    SourceLink = payoutSourceLink,
                    Date = item.Payout.Date,
                    PayoutId = item.Payout.Id,
                    Amount = _displayFormatter.Currency(payoutBlob.Amount, ppBlob?.Currency ?? PaymentMethodId.Parse(item.Payout.PaymentMethodId).CryptoCode),
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
