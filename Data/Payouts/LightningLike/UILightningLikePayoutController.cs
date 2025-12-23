using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.PayoutProcessors.Lightning;
using BTCPayServer.Payouts;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Data.Payouts.LightningLike
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    public class UILightningLikePayoutController : Controller
    {
        private readonly ApplicationDbContextFactory _applicationDbContextFactory;
        private readonly LightningAutomatedPayoutSenderFactory _lightningAutomatedPayoutSenderFactory;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;
        private readonly PayoutMethodHandlerDictionary _payoutHandlers;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly LightningClientFactoryService _lightningClientFactoryService;
        private readonly IOptions<LightningNetworkOptions> _options;
        private readonly IAuthorizationService _authorizationService;
        private readonly EventAggregator _eventAggregator;
        private readonly StoreRepository _storeRepository;

        public UILightningLikePayoutController(ApplicationDbContextFactory applicationDbContextFactory,
            LightningAutomatedPayoutSenderFactory lightningAutomatedPayoutSenderFactory,
            UserManager<ApplicationUser> userManager,
            BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings,
            PayoutMethodHandlerDictionary payoutHandlers,
            PaymentMethodHandlerDictionary handlers,
            StoreRepository storeRepository,
            LightningClientFactoryService lightningClientFactoryService,
            IOptions<LightningNetworkOptions> options,
            IAuthorizationService authorizationService,
            EventAggregator eventAggregator)
        {
            _applicationDbContextFactory = applicationDbContextFactory;
            _lightningAutomatedPayoutSenderFactory = lightningAutomatedPayoutSenderFactory;
            _userManager = userManager;
            _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
            _payoutHandlers = payoutHandlers;
            _handlers = handlers;
            _lightningClientFactoryService = lightningClientFactoryService;
            _options = options;
            _storeRepository = storeRepository;
            _authorizationService = authorizationService;
            _eventAggregator = eventAggregator;
        }

        private async Task<List<PayoutData>> GetPayouts(ApplicationDbContext dbContext, PayoutMethodId pmi,
            string[] payoutIds)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return new List<PayoutData>();
            }

            var pmiStr = pmi.ToString();

            var approvedStores = new Dictionary<string, bool>();

            return (await dbContext.Payouts
                    .Include(data => data.PullPaymentData)
                    .Include(data => data.StoreData)
                    .ThenInclude(data => data.UserStores)
                    .ThenInclude(data => data.StoreRole)
                    .Where(data =>
                        payoutIds.Contains(data.Id) &&
                        data.State == PayoutState.AwaitingPayment &&
                        data.PayoutMethodId == pmiStr)
                    .ToListAsync())
                .Where(payout =>
                {
                    if (approvedStores.TryGetValue(payout.StoreDataId, out var value))
                        return value;
                    value = payout.StoreData.UserStores
                        .Any(store => store.ApplicationUserId == userId && store.StoreRole.Permissions.Contains(Policies.CanModifyStoreSettings));
                    approvedStores.Add(payout.StoreDataId, value);
                    return value;
                }).ToList();
        }

        [HttpGet("pull-payments/payouts/lightning/{cryptoCode}")]
        public async Task<IActionResult> ConfirmLightningPayout(string cryptoCode, string[] payoutIds)
        {
            await SetStoreContext();

            var pmi = PayoutTypes.LN.GetPayoutMethodId(cryptoCode);

            await using var ctx = _applicationDbContextFactory.CreateContext();
            var payouts = await GetPayouts(ctx, pmi, payoutIds);

            var vm = payouts.Select(payoutData =>
            {
                var blob = payoutData.GetBlob(_btcPayNetworkJsonSerializerSettings);

                return new ConfirmVM
                {
                    Amount = payoutData.Amount.Value,
                    Destination = blob.Destination,
                    PayoutId = payoutData.Id
                };
            }).ToList();
            return View(vm);
        }

        [HttpPost("pull-payments/payouts/lightning/{cryptoCode}")]
        public async Task<IActionResult> ProcessLightningPayout(string cryptoCode, string[] payoutIds, CancellationToken cancellationToken)
        {
            await SetStoreContext();

            var pmi = PayoutTypes.LN.GetPayoutMethodId(cryptoCode);
            var paymentMethodId = PaymentTypes.LN.GetPaymentMethodId(cryptoCode);
            var payoutHandler = (LightningLikePayoutHandler)_payoutHandlers.TryGet(pmi);

            IEnumerable<IGrouping<string, PayoutData>> payouts;
            using (var ctx = _applicationDbContextFactory.CreateContext())
            {
                payouts = (await GetPayouts(ctx, pmi, payoutIds)).GroupBy(data => data.StoreDataId);
            }
            var results = new List<ResultVM>();

            //we group per store and init the transfers by each
            var authorizedForInternalNode = (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.CanModifyServerSettings))).Succeeded;
            foreach (var payoutDatas in payouts)
            {
                var store = payoutDatas.First().StoreData;
                var authorized = await _authorizationService.AuthorizeAsync(User, store, new PolicyRequirement(Policies.CanUseLightningNodeInStore));
                if (!authorized.Succeeded)
                {
                    results.AddRange(FailAll(payoutDatas, "You need the 'btcpay.store.canuselightningnode' permission for this action"));
                    continue;
                }
                var lightningSupportedPaymentMethod = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(paymentMethodId, _handlers);

                if (lightningSupportedPaymentMethod.IsInternalNode && !authorizedForInternalNode)
                {
                    results.AddRange(FailAll(payoutDatas, "You are currently using the internal Lightning node for this payout's store but you are not a server admin."));
                    continue;
                }
                var processor = _lightningAutomatedPayoutSenderFactory.ConstructProcessor(new PayoutProcessorData()
                {
                    Store = store,
                    StoreId = store.Id,
                    PayoutMethodId = pmi.ToString(),
                    Processor = LightningAutomatedPayoutSenderFactory.ProcessorName,
                    Id = Guid.NewGuid().ToString()
                });

                var client =
                    lightningSupportedPaymentMethod.CreateLightningClient(payoutHandler.Network, _options.Value,
                        _lightningClientFactoryService);

                foreach (var payout in payoutDatas)
                {
                    results.Add(await processor.HandlePayout(payout, client, cancellationToken));
                }
            }
            return View("LightningPayoutResult", results);
        }

        private ResultVM[] FailAll(IEnumerable<PayoutData> payouts, string message)
        {
            return payouts.Select(p => Fail(p, message)).ToArray();
        }
        private ResultVM Fail(PayoutData payoutData, string message)
        {
            var blob = payoutData.GetBlob(_btcPayNetworkJsonSerializerSettings);
            return new ResultVM
            {
                PayoutId = payoutData.Id,
                Success = false,
                Destination = blob.Destination,
                Message = message
            };
        }

        private async Task SetStoreContext()
        {
            var storeId = HttpContext.GetUserPrefsCookie()?.CurrentStoreId;
            if (string.IsNullOrEmpty(storeId))
                return;

            var userId = _userManager.GetUserId(User);
            var store = await _storeRepository.FindStore(storeId, userId);
            if (store != null)
            {
                HttpContext.SetStoreData(store);
            }
        }

        public class ResultVM
        {
            public string PayoutId { get; set; }
            public string Destination { get; set; }
            public bool? Success { get; set; }
            public string Message { get; set; }
        }

        public class ConfirmVM
        {
            public string PayoutId { get; set; }
            public string Destination { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
