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
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.PayoutProcessors;
using BTCPayServer.Payouts;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json.Linq;
using MarkPayoutRequest = BTCPayServer.HostedServices.MarkPayoutRequest;
using PayoutData = BTCPayServer.Data.PayoutData;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
    [Authorize(Policy = Policies.CanViewPullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    public class UIStorePullPaymentsController : Controller
    {
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly PayoutMethodHandlerDictionary _payoutHandlers;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly DisplayFormatter _displayFormatter;
        private readonly PullPaymentHostedService _pullPaymentService;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;
        private readonly IAuthorizationService _authorizationService;
        private readonly PayoutProcessorService _payoutProcessorService;
        private readonly IEnumerable<IPayoutProcessorFactory> _payoutProcessorFactories;

        public StoreData CurrentStore
        {
            get
            {
                return HttpContext.GetStoreData();
            }
        }

        public IStringLocalizer StringLocalizer { get; }

        public UIStorePullPaymentsController(BTCPayNetworkProvider btcPayNetworkProvider,
            IStringLocalizer stringLocalizer,
            PayoutMethodHandlerDictionary payoutHandlers,
            CurrencyNameTable currencyNameTable,
            DisplayFormatter displayFormatter,
            PullPaymentHostedService pullPaymentHostedService,
            ApplicationDbContextFactory dbContextFactory,
            PayoutProcessorService payoutProcessorService,
            IEnumerable<IPayoutProcessorFactory> payoutProcessorFactories,
            BTCPayNetworkJsonSerializerSettings jsonSerializerSettings,
            IAuthorizationService authorizationService)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
            StringLocalizer = stringLocalizer;
            _payoutHandlers = payoutHandlers;
            _currencyNameTable = currencyNameTable;
            _displayFormatter = displayFormatter;
            _pullPaymentService = pullPaymentHostedService;
            _dbContextFactory = dbContextFactory;
            _jsonSerializerSettings = jsonSerializerSettings;
            _authorizationService = authorizationService;
            _payoutProcessorService = payoutProcessorService;
            _payoutProcessorFactories = payoutProcessorFactories;
        }

        [HttpGet("stores/{storeId}/pull-payments/new")]
        [Authorize(Policy = Policies.CanCreateNonApprovedPullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public IActionResult NewPullPayment(string storeId)
        {
            if (CurrentStore is null)
                return NotFound();

            var paymentMethods = _payoutHandlers.GetSupportedPayoutMethods(CurrentStore);
            if (!paymentMethods.Any())
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = StringLocalizer["You must enable at least one payment method before creating a pull payment."].Value,
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(UIStoresController.Index), "UIStores", new { storeId });
            }

            return View(new NewPullPaymentModel
            {
                Name = "",
                Currency = CurrentStore.GetStoreBlob().DefaultCurrency,
                PayoutMethodsItem =
                    paymentMethods.Select(id => new SelectListItem(id.ToString(), id.ToString(), true))
            });
        }

        [HttpPost("stores/{storeId}/pull-payments/new")]
        [Authorize(Policy = Policies.CanCreateNonApprovedPullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> NewPullPayment(string storeId, NewPullPaymentModel model)
        {
            if (CurrentStore is null)
                return NotFound();

            var paymentMethodOptions = _payoutHandlers.GetSupportedPayoutMethods(CurrentStore);
            model.PayoutMethodsItem =
                paymentMethodOptions.Select(id => new SelectListItem(id.ToString(), id.ToString(), true));
            model.Name ??= string.Empty;
            model.Currency = model.Currency?.ToUpperInvariant()?.Trim() ?? String.Empty;
            model.PayoutMethods ??= new List<string>();
            if (!model.PayoutMethods.Any())
            {
                // Since we assign all payment methods to be selected by default above we need to update
                // them here to reflect user's selection so that they can correct their mistake
                model.PayoutMethodsItem =
                    paymentMethodOptions.Select(id => new SelectListItem(id.ToString(), id.ToString(), false));
                ModelState.AddModelError(nameof(model.PayoutMethods), StringLocalizer["You need at least one payout method"]);
            }
            if (_currencyNameTable.GetCurrencyData(model.Currency, false) is null)
            {
                ModelState.AddModelError(nameof(model.Currency), StringLocalizer["Invalid currency"]);
            }
            if (model.Amount <= 0.0m)
            {
                ModelState.AddModelError(nameof(model.Amount), StringLocalizer["The amount should be more than zero"]);
            }
            if (model.Name.Length > 50)
            {
                ModelState.AddModelError(nameof(model.Name), StringLocalizer["The name should be maximum 50 characters."]);
            }

            var selectedPaymentMethodIds = model.PayoutMethods.Select(PayoutMethodId.Parse).ToArray();
            if (!selectedPaymentMethodIds.All(id => paymentMethodOptions.Contains(id)))
            {
                ModelState.AddModelError(nameof(model.Name), StringLocalizer["Not all payout methods are supported"]);
            }
            if (!ModelState.IsValid)
                return View(model);
            model.AutoApproveClaims = model.AutoApproveClaims &&  (await
                _authorizationService.AuthorizeAsync(User, storeId, Policies.CanCreatePullPayments)).Succeeded;
            await _pullPaymentService.CreatePullPayment(CurrentStore, new CreatePullPaymentRequest
            {
                Name = model.Name,
                Description = model.Description,
                Amount = model.Amount,
                Currency = model.Currency,
                PayoutMethods = selectedPaymentMethodIds.Select(p => p.ToString()).ToArray(),
                BOLT11Expiration = TimeSpan.FromDays(model.BOLT11Expiration),
                AutoApproveClaims = model.AutoApproveClaims
            });
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = StringLocalizer["Pull payment request created"].Value,
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction(nameof(PullPayments), new { storeId });
        }

        [Authorize(Policy = Policies.CanViewPullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
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

            var paymentMethods = _payoutHandlers.GetSupportedPayoutMethods(HttpContext.GetStoreData());
            if (!paymentMethods.Any())
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = StringLocalizer["You must enable at least one payment method before creating a pull payment."].Value,
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(UIStoresController.Index), "UIStores", new { storeId });
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
                new ConfirmModel(StringLocalizer["Archive pull payment"], StringLocalizer["Do you really want to archive the pull payment?"], StringLocalizer["Archive"]));
        }

        [HttpPost("stores/{storeId}/pull-payments/{pullPaymentId}/archive")]
        [Authorize(Policy = Policies.CanArchivePullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> ArchivePullPaymentPost(string storeId,
            string pullPaymentId)
        {
            await _pullPaymentService.Cancel(new PullPaymentHostedService.CancelRequest(pullPaymentId));
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = StringLocalizer["Pull payment archived"].Value,
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

            vm.PayoutMethods = _payoutHandlers.GetSupportedPayoutMethods(HttpContext.GetStoreData());
            vm.HasPayoutProcessor = await HasPayoutProcessor(storeId, vm.PayoutMethodId);
            var payoutMethodId = PayoutMethodId.Parse(vm.PayoutMethodId);
            var handler = _payoutHandlers
                .TryGet(payoutMethodId);
            var commandState = Enum.Parse<PayoutState>(vm.Command.Split("-").First());
            var payoutIds = vm.GetSelectedPayouts(commandState);
            if (payoutIds.Length == 0)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = StringLocalizer["No payout selected"].Value,
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(Payouts),
                    new
                    {
                        storeId = storeId,
                        pullPaymentId = vm.PullPaymentId,
                        payoutMethodId = payoutMethodId.ToString()
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
                            await GetPayoutsForPaymentMethod(payoutMethodId, ctx, payoutIds, storeId, cancellationToken);

                        var failed = false;
                        for (int i = 0; i < payouts.Count; i++)
                        {
                            var payout = payouts[i];
                            if (payout.State != PayoutState.AwaitingApproval)
                                continue;
                            var rateResult = await _pullPaymentService.GetRate(payout, null, cancellationToken);
                            if (rateResult.BidAsk == null)
                            {
                                TempData.SetStatusMessageModel(new StatusMessageModel()
                                {
                                    Message = StringLocalizer["Rate unavailable: {0}", rateResult.EvaluatedRule].Value,
                                    Severity = StatusMessageModel.StatusSeverity.Error
                                });
                                failed = true;
                                break;
                            }

                            var approveResult = await _pullPaymentService.Approve(
                                new PullPaymentHostedService.PayoutApproval
                                {
                                    PayoutId = payout.Id,
                                    Revision = payout.GetBlob(_jsonSerializerSettings).Revision,
                                    Rate = rateResult.BidAsk.Ask
                                });
                            if (approveResult.Result != PullPaymentHostedService.PayoutApproval.Result.Ok)
                            {
                                TempData.SetStatusMessageModel(new StatusMessageModel
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

                        if (command == "approve-pay" && !vm.HasPayoutProcessor)
                        {
                            goto case "pay";
                        }

                        TempData.SetStatusMessageModel(new StatusMessageModel
                        {
                            Message = StringLocalizer["Payouts approved"].Value,
                            Severity = StatusMessageModel.StatusSeverity.Success
                        });
                        break;
                    }

                case "pay":
                    {
                        if (handler is { })
                            return await handler.InitiatePayment(payoutIds);
                        TempData.SetStatusMessageModel(new StatusMessageModel
                        {
                            Message = StringLocalizer["Paying via this payment method is not supported"].Value,
                            Severity = StatusMessageModel.StatusSeverity.Error
                        });
                        break;
                    }

                case "mark-paid":
                    {
                        await using var ctx = this._dbContextFactory.CreateContext();
                        ctx.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                        var payouts =
                            await GetPayoutsForPaymentMethod(payoutMethodId, ctx, payoutIds, storeId, cancellationToken);
                        for (int i = 0; i < payouts.Count; i++)
                        {
                            var payout = payouts[i];
                            if (payout.State != PayoutState.AwaitingPayment)
                                continue;

                            var result =
                                await _pullPaymentService.MarkPaid(new MarkPayoutRequest { PayoutId = payout.Id });
                            if (result != MarkPayoutRequest.PayoutPaidResult.Ok)
                            {
                                TempData.SetStatusMessageModel(new StatusMessageModel
                                {
                                    Message = MarkPayoutRequest.GetErrorMessage(result),
                                    Severity = StatusMessageModel.StatusSeverity.Error
                                });
                                return RedirectToAction(nameof(Payouts),
                                    new
                                    {
                                        storeId = storeId,
                                        pullPaymentId = vm.PullPaymentId,
                                        payoutMethodId = payoutMethodId.ToString()
                                    });
                            }
                        }

                        TempData.SetStatusMessageModel(new StatusMessageModel
                        {
                            Message = StringLocalizer["Payouts marked as paid"].Value,
                            Severity = StatusMessageModel.StatusSeverity.Success
                        });
                        break;
                    }
                case "mark-awaiting-payment":
                    await using (var context = _dbContextFactory.CreateContext())
                    {
                        var payouts = (await PullPaymentHostedService.GetPayouts(new PullPaymentHostedService.PayoutQuery()
                        {
                            States = new[] { PayoutState.InProgress },
                            Stores = new[] { storeId },
                            PayoutIds = payoutIds
                        }, context));
                        foreach (var payout in payouts)
                        {
                            payout.State = PayoutState.AwaitingPayment;
                            payout.Proof = null;
                        }
                        await context.SaveChangesAsync();
                    }
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Message = "Payout payments have been marked as awaiting payment",
                        Severity = StatusMessageModel.StatusSeverity.Success
                    });
                    break;
                case "cancel":
                    await _pullPaymentService.Cancel(
                        new PullPaymentHostedService.CancelRequest(payoutIds, new[] { storeId }));
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Message = StringLocalizer["Payouts archived"].Value,
                        Severity = StatusMessageModel.StatusSeverity.Success
                    });
                    break;
            }

            return RedirectToAction(nameof(Payouts),
                new
                {
                    storeId = storeId,
                    pullPaymentId = vm.PullPaymentId,
                    payoutMethodId = payoutMethodId.ToString()
                });
        }

        private static async Task<List<PayoutData>> GetPayoutsForPaymentMethod(PayoutMethodId payoutMethodId,
            ApplicationDbContext ctx, string[] payoutIds,
            string storeId, CancellationToken cancellationToken)
        {
            return await PullPaymentHostedService.GetPayouts(new PullPaymentHostedService.PayoutQuery()
            {
                IncludeArchived = false,
                IncludeStoreData = true,
                Stores = new[] { storeId },
                PayoutIds = payoutIds,
                PayoutMethods = new[] { payoutMethodId.ToString() }
            }, ctx, cancellationToken);
        }

        [HttpGet("stores/{storeId}/pull-payments/{pullPaymentId}/payouts")]
        [HttpGet("stores/{storeId}/payouts")]
        [Authorize(Policy = Policies.CanViewPayouts, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> Payouts(
            string storeId, string pullPaymentId, string payoutMethodId, PayoutState payoutState,
            int skip = 0, int count = 50)
        {
            var paymentMethods = _payoutHandlers.GetSupportedPayoutMethods(HttpContext.GetStoreData());
            if (!paymentMethods.Any())
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = StringLocalizer["You must enable at least one payment method before creating a payout."].Value,
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(UIStoresController.Index), "UIStores", new { storeId });
            }

            payoutMethodId ??= paymentMethods.First().ToString();
            var vm = this.ParseListQuery(new PayoutsModel
            {
                PayoutMethods = paymentMethods,
                PayoutMethodId = payoutMethodId,
                PullPaymentId = pullPaymentId,
                PayoutState = payoutState,
                Skip = skip,
                Count = count,
                Payouts = new List<PayoutsModel.PayoutModel>(),
                HasPayoutProcessor = await HasPayoutProcessor(storeId, payoutMethodId)
            });
            await using var ctx = _dbContextFactory.CreateContext();
            var payoutRequest =
                ctx.Payouts.Where(p => p.StoreDataId == storeId && (p.PullPaymentDataId == null || !p.PullPaymentData.Archived));
            if (pullPaymentId != null)
            {
                payoutRequest = payoutRequest.Where(p => p.PullPaymentDataId == vm.PullPaymentId);
                vm.PullPaymentName = (await ctx.PullPayments.FindAsync(pullPaymentId)).GetBlob().Name;
            }

            vm.PayoutMethodCount = (await payoutRequest.GroupBy(data => data.PayoutMethodId)
                    .Select(datas => new { datas.Key, Count = datas.Count() }).ToListAsync())
                .ToDictionary(datas => datas.Key, arg => arg.Count);

            if (vm.PayoutMethodId != null)
            {
                var pmiStr = vm.PayoutMethodId;
                payoutRequest = payoutRequest.Where(p => p.PayoutMethodId == pmiStr);
            }
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
                    Amount = _displayFormatter.Currency(item.Payout.OriginalAmount, item.Payout.OriginalCurrency),
                    Destination = payoutBlob.Destination
                };
                var handler = _payoutHandlers
                    .TryGet(item.Payout.GetPayoutMethodId());
                var proofBlob = handler?.ParseProof(item.Payout);
                m.ProofLink = proofBlob?.Link;
                vm.Payouts.Add(m);
            }
            return View(vm);
        }

        private async Task<bool> HasPayoutProcessor(string storeId, PayoutMethodId payoutMethodId)
        {
            var processors = await _payoutProcessorService.GetProcessors(
                new PayoutProcessorService.PayoutProcessorQuery { Stores = [storeId], PayoutMethods = [payoutMethodId] });
            return _payoutProcessorFactories.Any(factory => factory.GetSupportedPayoutMethods().Contains(payoutMethodId)) && processors.Any();
        }
        private async Task<bool> HasPayoutProcessor(string storeId, string payoutMethodId)
        {
            return PayoutMethodId.TryParse(payoutMethodId, out var pmId) && await HasPayoutProcessor(storeId, pmId);
        }
    }
}
