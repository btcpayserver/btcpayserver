using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using LNURL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace BTCPayServer.Data.Payouts.LightningLike
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    public class UILightningLikePayoutController : Controller
    {
        private readonly ApplicationDbContextFactory _applicationDbContextFactory;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;
        private readonly IEnumerable<IPayoutHandler> _payoutHandlers;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly LightningClientFactoryService _lightningClientFactoryService;
        private readonly IOptions<LightningNetworkOptions> _options;
        private readonly IAuthorizationService _authorizationService;
        private readonly EventAggregator _eventAggregator;
        private readonly StoreRepository _storeRepository;

        public UILightningLikePayoutController(ApplicationDbContextFactory applicationDbContextFactory,
            UserManager<ApplicationUser> userManager,
            BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings,
            IEnumerable<IPayoutHandler> payoutHandlers,
            BTCPayNetworkProvider btcPayNetworkProvider,
            StoreRepository storeRepository,
            LightningClientFactoryService lightningClientFactoryService,
            IOptions<LightningNetworkOptions> options, 
            IAuthorizationService authorizationService,
            EventAggregator eventAggregator)
        {
            _applicationDbContextFactory = applicationDbContextFactory;
            _userManager = userManager;
            _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
            _payoutHandlers = payoutHandlers;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _lightningClientFactoryService = lightningClientFactoryService;
            _options = options;
            _storeRepository = storeRepository;
            _authorizationService = authorizationService;
            _eventAggregator = eventAggregator;
        }

        private async Task<List<PayoutData>> GetPayouts(ApplicationDbContext dbContext, PaymentMethodId pmi,
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
                        data.PaymentMethodId == pmiStr)
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

            var pmi = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);

            await using var ctx = _applicationDbContextFactory.CreateContext();
            var payouts = await GetPayouts(ctx, pmi, payoutIds);

            var vm = payouts.Select(payoutData =>
            {
                var blob = payoutData.GetBlob(_btcPayNetworkJsonSerializerSettings);

                return new ConfirmVM
                {
                    Amount = blob.CryptoAmount.Value,
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

            var pmi = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
            var payoutHandler = (LightningLikePayoutHandler)_payoutHandlers.FindPayoutHandler(pmi);

            await using var ctx = _applicationDbContextFactory.CreateContext();

            var payouts = (await GetPayouts(ctx, pmi, payoutIds)).GroupBy(data => data.StoreDataId);
            var results = new List<ResultVM>();
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(pmi.CryptoCode);

            //we group per store and init the transfers by each

            var authorizedForInternalNode = (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.CanModifyServerSettings))).Succeeded;
            foreach (var payoutDatas in payouts)
            {
                var store = payoutDatas.First().StoreData;

                var lightningSupportedPaymentMethod = store.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                    .OfType<LightningSupportedPaymentMethod>()
                    .FirstOrDefault(method => method.PaymentId == pmi);

                if (lightningSupportedPaymentMethod.IsInternalNode && !authorizedForInternalNode)
                {
                    foreach (PayoutData payoutData in payoutDatas)
                    {

                        var blob = payoutData.GetBlob(_btcPayNetworkJsonSerializerSettings);
                        results.Add(new ResultVM
                        {
                            PayoutId = payoutData.Id,
                            Result = PayResult.Error,
                            Destination = blob.Destination,
                            Message = "You are currently using the internal Lightning node for this payout's store but you are not a server admin."
                        });
                    }

                    continue;
                }

                var client =
                    lightningSupportedPaymentMethod.CreateLightningClient(network, _options.Value,
                        _lightningClientFactoryService);
                foreach (var payoutData in payoutDatas)
                {
                    ResultVM result;
                    var blob = payoutData.GetBlob(_btcPayNetworkJsonSerializerSettings);
                    var claim = await payoutHandler.ParseClaimDestination(pmi, blob.Destination, cancellationToken);
                    try
                    {
                        switch (claim.destination)
                        {
                            case LNURLPayClaimDestinaton lnurlPayClaimDestinaton:
                                var lnurlResult = await GetInvoiceFromLNURL(payoutData, payoutHandler, blob,
                                    lnurlPayClaimDestinaton, network.NBitcoinNetwork, cancellationToken);
                                if (lnurlResult.Item2 is not null)
                                {
                                    result = lnurlResult.Item2;
                                }
                                else
                                {
                                    result = await TrypayBolt(client, blob, payoutData, lnurlResult.Item1, pmi, cancellationToken);
                                }

                                break;

                            case BoltInvoiceClaimDestination item1:
                                result = await TrypayBolt(client, blob, payoutData, item1.PaymentRequest, pmi, cancellationToken);

                                break;
                            default:
                                result = new ResultVM
                                {
                                    PayoutId = payoutData.Id,
                                    Result = PayResult.Error,
                                    Destination = blob.Destination,
                                    Message = claim.error
                                };
                                break;
                        }
                    }
                    catch (Exception exception)
                    {
                        result = new ResultVM
                        {
                            PayoutId = payoutData.Id,
                            Result = PayResult.Error,
                            Destination = blob.Destination,
                            Message = exception.Message
                        };
                    }
                    results.Add(result);
                }
            }

            await ctx.SaveChangesAsync();
            foreach (var payoutG in payouts)
            {
                foreach (PayoutData payout in payoutG)
                {
                    if (payout.State != PayoutState.AwaitingPayment)
                    {
                        _eventAggregator.Publish(new PayoutEvent(null, payout));
                    }
                }
            }
            return View("LightningPayoutResult", results);
        }
        public static async Task<(BOLT11PaymentRequest, ResultVM)> GetInvoiceFromLNURL(PayoutData payoutData,
            LightningLikePayoutHandler handler, PayoutBlob blob, LNURLPayClaimDestinaton lnurlPayClaimDestinaton, Network network, CancellationToken cancellationToken)
        {
            var endpoint = lnurlPayClaimDestinaton.LNURL.IsValidEmail()
                ? LNURL.LNURL.ExtractUriFromInternetIdentifier(lnurlPayClaimDestinaton.LNURL)
                : LNURL.LNURL.Parse(lnurlPayClaimDestinaton.LNURL, out _);
            var httpClient = handler.CreateClient(endpoint);
            var lnurlInfo =
                (LNURLPayRequest)await LNURL.LNURL.FetchInformation(endpoint, "payRequest",
                    httpClient, cancellationToken);
            var lm = new LightMoney(blob.CryptoAmount.Value, LightMoneyUnit.BTC);
            if (lm > lnurlInfo.MaxSendable || lm < lnurlInfo.MinSendable)
            {
                return (null, new ResultVM
                {
                    PayoutId = payoutData.Id,
                    Result = PayResult.Error,
                    Destination = blob.Destination,
                    Message =
                        $"The LNURL provided would not generate an invoice of {lm.MilliSatoshi}msats"
                });
            }

            try
            {
                var lnurlPayRequestCallbackResponse =
                    await lnurlInfo.SendRequest(lm, network, httpClient, cancellationToken: cancellationToken);

                return (lnurlPayRequestCallbackResponse.GetPaymentRequest(network), null);
            }
            catch (LNUrlException e)
            {
                return (null,
                    new ResultVM
                    {
                        PayoutId = payoutData.Id,
                        Result = PayResult.Error,
                        Destination = blob.Destination,
                        Message = e.Message
                    });
            }
        }

        public static async Task<ResultVM> TrypayBolt(
            ILightningClient lightningClient, PayoutBlob payoutBlob, PayoutData payoutData, BOLT11PaymentRequest bolt11PaymentRequest,
            PaymentMethodId pmi, CancellationToken cancellationToken)
        {
            var boltAmount = bolt11PaymentRequest.MinimumAmount.ToDecimal(LightMoneyUnit.BTC);
            if (boltAmount > payoutBlob.CryptoAmount)
            {

                payoutData.State = PayoutState.Cancelled;
                return new ResultVM
                {
                    PayoutId = payoutData.Id,
                    Result = PayResult.Error,
                    Message = $"The BOLT11 invoice amount ({boltAmount} {pmi.CryptoCode}) did not match the payout's amount ({payoutBlob.CryptoAmount.GetValueOrDefault()} {pmi.CryptoCode})",
                    Destination = payoutBlob.Destination
                };
            }

            if (bolt11PaymentRequest.ExpiryDate < DateTimeOffset.Now)
            {
                payoutData.State = PayoutState.Cancelled;
                return new ResultVM
                {
                    PayoutId = payoutData.Id,
                    Result = PayResult.Error,
                    Message = $"The BOLT11 invoice expiry date ({bolt11PaymentRequest.ExpiryDate}) has expired",
                    Destination = payoutBlob.Destination
                };
            }

            var proofBlob = new PayoutLightningBlob { PaymentHash = bolt11PaymentRequest.PaymentHash.ToString() };
            try
            {
                var result = await lightningClient.Pay(bolt11PaymentRequest.ToString(),
                    new PayInvoiceParams()
                    {
                        // CLN does not support explicit amount param if it is the same as the invoice amount
                        Amount = payoutBlob.CryptoAmount == bolt11PaymentRequest.MinimumAmount.ToDecimal(LightMoneyUnit.BTC)? null: new LightMoney((decimal)payoutBlob.CryptoAmount, LightMoneyUnit.BTC)
                    }, cancellationToken);
                if (result == null) throw new NoPaymentResultException();
                
                string message = null;
                if (result.Result == PayResult.Ok)
                {
                    message = result.Details?.TotalAmount != null
                        ? $"Paid out {result.Details.TotalAmount.ToDecimal(LightMoneyUnit.BTC)}"
                        : null;
                    payoutData.State = PayoutState.Completed;
                    try
                    {
                        var payment = await lightningClient.GetPayment(bolt11PaymentRequest.PaymentHash.ToString(), cancellationToken);
                        proofBlob.Preimage = payment.Preimage;
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                }
                else if (result.Result == PayResult.Unknown)
                {
                    payoutData.State = PayoutState.InProgress;
                    message = "The payment has been initiated but is still in-flight.";
                }

                payoutData.SetProofBlob(proofBlob, null);
                return new ResultVM
                {
                    PayoutId = payoutData.Id,
                    Result = result.Result,
                    Destination = payoutBlob.Destination,
                    Message = message
                };
            }
            catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException or NoPaymentResultException)
            {
                // Timeout, potentially caused by hold invoices
                // Payment will be saved as pending, the LightningPendingPayoutListener will handle settling/cancelling
                payoutData.State = PayoutState.InProgress;

                payoutData.SetProofBlob(proofBlob, null);
                return new ResultVM
                {
                    PayoutId = payoutData.Id,
                    Result = PayResult.Ok,
                    Destination = payoutBlob.Destination,
                    Message = "The payment timed out. We will verify if it completed later."
                };
            }
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
            public PayResult Result { get; set; }
            public string Message { get; set; }
        }

        public class ConfirmVM
        {
            public string PayoutId { get; set; }
            public string Destination { get; set; }
            public decimal Amount { get; set; }
        }
    }

    public class NoPaymentResultException : Exception
    {
    }
}
