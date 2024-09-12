using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3.Model;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.NTag424;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using NdefLibrary.Ndef;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers
{
    public partial class UIPullPaymentController : Controller
    {
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly DisplayFormatter _displayFormatter;
        private readonly UriResolver _uriResolver;
        private readonly PullPaymentHostedService _pullPaymentHostedService;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly BTCPayNetworkJsonSerializerSettings _serializerSettings;
        private readonly PayoutMethodHandlerDictionary _payoutHandlers;
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayServerEnvironment _env;
        private readonly SettingsRepository _settingsRepository;

        public UIPullPaymentController(ApplicationDbContextFactory dbContextFactory,
            CurrencyNameTable currencyNameTable,
            DisplayFormatter displayFormatter,
            UriResolver uriResolver,
            PullPaymentHostedService pullPaymentHostedService,
            BTCPayNetworkProvider networkProvider,
            BTCPayNetworkJsonSerializerSettings serializerSettings,
            PayoutMethodHandlerDictionary payoutHandlers,
            StoreRepository storeRepository,
            BTCPayServerEnvironment env,
            SettingsRepository settingsRepository)
        {
            _dbContextFactory = dbContextFactory;
            _currencyNameTable = currencyNameTable;
            _displayFormatter = displayFormatter;
            _uriResolver = uriResolver;
            _pullPaymentHostedService = pullPaymentHostedService;
            _serializerSettings = serializerSettings;
            _payoutHandlers = payoutHandlers;
            _storeRepository = storeRepository;
            _env = env;
            _settingsRepository = settingsRepository;
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

            var store = await _storeRepository.FindStore(pp.StoreId);
            if (store is null)
                return NotFound();

            var storeBlob = store.GetStoreBlob();
            var payouts = (await ctx.Payouts.Where(p => p.PullPaymentDataId == pp.Id)
                    .OrderByDescending(o => o.Date)
                    .ToListAsync())
                .Select(o => new
                {
                    Entity = o,
                    Blob = o.GetBlob(_serializerSettings),
                    ProofBlob = _payoutHandlers.TryGet(o.GetPayoutMethodId())?.ParseProof(o)
                });
            var cd = _currencyNameTable.GetCurrencyData(pp.Currency, false);
            var totalPaid = payouts.Where(p => p.Entity.State != PayoutState.Cancelled).Select(p => p.Entity.OriginalAmount).Sum();
            var amountDue = pp.Limit - totalPaid;

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
                    Amount = entity.Entity.OriginalAmount,
                    AmountFormatted = _displayFormatter.Currency(entity.Entity.OriginalAmount, entity.Entity.OriginalCurrency),
                    Currency = entity.Entity.OriginalCurrency,
                    Status = entity.Entity.State,
                    Destination = entity.Blob.Destination,
                    PaymentMethod = PaymentMethodId.Parse(entity.Entity.PayoutMethodId),
                    Link = entity.ProofBlob?.Link,
                    TransactionId = entity.ProofBlob?.Id
                }).ToList()
            };
            vm.IsPending &= vm.AmountDue > 0.0m;
            vm.StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob);
            
            if (_pullPaymentHostedService.SupportsLNURL(pp))
            {
                var url = Url.Action(nameof(UILNURLController.GetLNURLForPullPayment), "UILNURL", new { cryptoCode = _networkProvider.DefaultNetwork.CryptoCode, pullPaymentId = vm.Id }, Request.Scheme, Request.Host.ToString());
                vm.LnurlEndpoint = url != null ? new Uri(url) : null;
                vm.SetupDeepLink = $"boltcard://program?url={GetBoltcardDeeplinkUrl(vm, OnExistingBehavior.UpdateVersion)}";
                vm.ResetDeepLink = $"boltcard://reset?url={GetBoltcardDeeplinkUrl(vm, OnExistingBehavior.KeepVersion)}";
            }

            return View(nameof(ViewPullPayment), vm);
        }

        private string GetBoltcardDeeplinkUrl(ViewPullPaymentModel vm, OnExistingBehavior onExisting)
        {
            var registerUrl = Url.Action(nameof(GreenfieldPullPaymentController.RegisterBoltcard), "GreenfieldPullPayment",
                            new
                            {
                                pullPaymentId = vm.Id,
                                onExisting = onExisting.ToString()
                            }, Request.Scheme, Request.Host.ToString());
            registerUrl = Uri.EscapeDataString(registerUrl);
            return registerUrl;
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
            blob.View = new PullPaymentBlob.PullPaymentView
            {
                Title = viewModel.Name ?? string.Empty,
                Description = viewModel.Description ?? string.Empty,
                Email = null
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
            var supported = ppBlob.SupportedPayoutMethods;
            PayoutMethodId payoutMethodId = null;
            IClaimDestination destination = null;
            IPayoutHandler payoutHandler = null;
            string error = null;
            if (string.IsNullOrEmpty(vm.SelectedPayoutMethod))
            {
                foreach (var pmId in supported)
                {
                    var handler = _payoutHandlers.TryGet(pmId);
                    (IClaimDestination dst, string err) = handler == null
                        ? (null, "No payment handler found for this payment method")
                        : await handler.ParseAndValidateClaimDestination(vm.Destination, ppBlob, cancellationToken);
                    error = err;
                    if (dst is not null && err is null)
                    {
                        payoutMethodId = pmId;
                        destination = dst;
                        payoutHandler = handler;
                        break;
                    }
                }
            }
            else
            {
                payoutMethodId = supported.FirstOrDefault(id => vm.SelectedPayoutMethod == id.ToString());
                payoutHandler = payoutMethodId is null ? null : _payoutHandlers.TryGet(payoutMethodId);
                if (payoutHandler is not null)
                {
                    (destination, error) = await payoutHandler.ParseAndValidateClaimDestination(vm.Destination, ppBlob, cancellationToken);
                }
            }

            if (destination is null)
            {
                ModelState.AddModelError(nameof(vm.Destination), error ?? "Invalid destination or payment method");
                return await ViewPullPayment(pullPaymentId);
            }
            var amtError = ClaimRequest.IsPayoutAmountOk(destination, vm.ClaimedAmount == 0 ? null : vm.ClaimedAmount, payoutHandler.Currency, pp.Currency);
            if (amtError.error is not null)
            {
                ModelState.AddModelError(nameof(vm.ClaimedAmount), amtError.error);
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
                PayoutMethodId = payoutMethodId,
                StoreId = pp.StoreId
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
                Message = $"Your claim request of {_displayFormatter.Currency(vm.ClaimedAmount, pp.Currency, DisplayFormatter.CurrencyFormat.Symbol)} to {vm.Destination} has been submitted and is awaiting {(result.PayoutData.State == PayoutState.AwaitingApproval ? "approval" : "payment")}.",
                Severity = StatusMessageModel.StatusSeverity.Success
            });

            return RedirectToAction(nameof(ViewPullPayment), new { pullPaymentId });
        }
    }
}
