using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using LNURL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Data.Payouts.LightningLike
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    public class LightningLikePayoutController : Controller
    {
        private readonly ApplicationDbContextFactory _applicationDbContextFactory;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;
        private readonly IEnumerable<IPayoutHandler> _payoutHandlers;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly LightningClientFactoryService _lightningClientFactoryService;
        private readonly IOptions<LightningNetworkOptions> _options;

        public LightningLikePayoutController(ApplicationDbContextFactory applicationDbContextFactory,
            UserManager<ApplicationUser> userManager,
            BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings,
            IEnumerable<IPayoutHandler> payoutHandlers,
            BTCPayNetworkProvider btcPayNetworkProvider,
            LightningClientFactoryService lightningClientFactoryService,
            IOptions<LightningNetworkOptions> options)
        {
            _applicationDbContextFactory = applicationDbContextFactory;
            _userManager = userManager;
            _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
            _payoutHandlers = payoutHandlers;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _lightningClientFactoryService = lightningClientFactoryService;
            _options = options;
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
                    .ThenInclude(data => data.StoreData)
                    .ThenInclude(data => data.UserStores)
                    .Where(data =>
                        payoutIds.Contains(data.Id) &&
                        data.State == PayoutState.AwaitingPayment &&
                        data.PaymentMethodId == pmiStr)
                    .ToListAsync())
                .Where(payout =>
                {
                    if (approvedStores.TryGetValue(payout.PullPaymentData.StoreId, out var value)) return value;
                    value = payout.PullPaymentData.StoreData.UserStores
                        .Any(store => store.Role == StoreRoles.Owner && store.ApplicationUserId == userId);
                    approvedStores.Add(payout.PullPaymentData.StoreId, value);
                    return value;
                }).ToList();
        }

        [HttpGet("pull-payments/payouts/lightning/{cryptoCode}")]
        public async Task<IActionResult> ConfirmLightningPayout(string cryptoCode, string[] payoutIds)
        {
            var pmi = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);

            await using var ctx = _applicationDbContextFactory.CreateContext();
            var payouts = await GetPayouts(ctx, pmi, payoutIds);

            var vm = payouts.Select(payoutData =>
            {
                var blob = payoutData.GetBlob(_btcPayNetworkJsonSerializerSettings);

                return new ConfirmVM()
                {
                    Amount = blob.CryptoAmount.Value, Destination = blob.Destination, PayoutId = payoutData.Id
                };
            }).ToList();
            return View(vm);
        }

        [HttpPost("pull-payments/payouts/lightning/{cryptoCode}")]
        public async Task<IActionResult> ProcessLightningPayout(string cryptoCode, string[] payoutIds)
        {
            var pmi = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
            var payoutHandler = _payoutHandlers.FindPayoutHandler(pmi);

            await using var ctx = _applicationDbContextFactory.CreateContext();

            var payouts = (await GetPayouts(ctx, pmi, payoutIds)).GroupBy(data => data.PullPaymentData.StoreId);
            var results = new List<ResultVM>();
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(pmi.CryptoCode);

            //we group per store and init the transfers by each
            async Task TrypayBolt(ILightningClient lightningClient, PayoutBlob payoutBlob, PayoutData payoutData, BOLT11PaymentRequest bolt11PaymentRequest)
            {
                var boltAmount = bolt11PaymentRequest.MinimumAmount.ToDecimal(LightMoneyUnit.BTC);
                if (boltAmount != payoutBlob.CryptoAmount)
                {
                    results.Add(new ResultVM()
                    {
                        PayoutId = payoutData.Id, 
                        Result = PayResult.Error,
                        Message = $"The BOLT11 invoice amount did not match the payout's amount ({boltAmount} instead of {payoutBlob.CryptoAmount})", 
                        Destination = payoutBlob.Destination
                    });
                    return;
                }
                var result = await lightningClient.Pay(bolt11PaymentRequest.ToString());
                if (result.Result == PayResult.Ok)
                {
                    results.Add(new ResultVM()
                    {
                        PayoutId = payoutData.Id, Result = result.Result, Destination = payoutBlob.Destination
                    });
                    payoutData.State = PayoutState.Completed;
                }
                else
                {
                    results.Add(new ResultVM()
                    {
                        PayoutId = payoutData.Id, Result = result.Result, Destination = payoutBlob.Destination
                    });
                }
            }

            foreach (var payoutDatas in payouts)
            {
                var store = payoutDatas.First().PullPaymentData.StoreData;
                var lightningSupportedPaymentMethod = store.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                    .OfType<LightningSupportedPaymentMethod>()
                    .FirstOrDefault(method => method.PaymentId == pmi);
                var client =
                    lightningSupportedPaymentMethod.CreateLightningClient(network, _options.Value,
                        _lightningClientFactoryService);
                foreach (var payoutData in payoutDatas)
                {
                    var blob = payoutData.GetBlob(_btcPayNetworkJsonSerializerSettings);
                    var claim = await payoutHandler.ParseClaimDestination(pmi, blob.Destination, false);
                    try
                    {
                        switch (claim.destination)
                        {
                            case LNURLPayClaimDestinaton lnurlPayClaimDestinaton:
                                var endpoint = LNURL.LNURL.Parse(lnurlPayClaimDestinaton.LNURL, out var tag);
                                var lightningPayoutHandler = (LightningLikePayoutHandler)payoutHandler;
                                var httpClient = lightningPayoutHandler.CreateClient(endpoint);
                                var lnurlInfo =
                                    (LNURLPayRequest)await LNURL.LNURL.FetchInformation(endpoint, "payRequest",
                                        httpClient);
                                var lm = new LightMoney(blob.CryptoAmount.Value, LightMoneyUnit.BTC);
                                if (lm > lnurlInfo.MaxSendable || lm < lnurlInfo.MinSendable)
                                {
                                    results.Add(new ResultVM()
                                    {
                                        PayoutId = payoutData.Id,
                                        Result = PayResult.Error,
                                        Destination = blob.Destination,
                                        Message =
                                            $"The LNURL provided would not generate an invoice of {lm.MilliSatoshi}msats"
                                    });
                                }
                                else
                                {
                                    try
                                    {
                                        var lnurlPayRequestCallbackResponse =
                                            await lnurlInfo.SendRequest(lm, network.NBitcoinNetwork, httpClient);
                                        
                                        await TrypayBolt(client, blob, payoutData, lnurlPayRequestCallbackResponse.GetPaymentRequest(network.NBitcoinNetwork));
                                    }
                                    catch (LNUrlException e)
                                    {
                                        results.Add(new ResultVM()
                                        {
                                            PayoutId = payoutData.Id,
                                            Result = PayResult.Error,
                                            Destination = blob.Destination,
                                            Message = e.Message
                                        });
                                    }
                                }

                                break;

                            case BoltInvoiceClaimDestination item1:
                                await TrypayBolt(client, blob, payoutData, item1.PaymentRequest);

                                break;
                            default:
                                results.Add(new ResultVM()
                                {
                                    PayoutId = payoutData.Id,
                                    Result = PayResult.Error,
                                    Destination = blob.Destination,
                                    Message = claim.error
                                });
                                break;
                        }
                    }
                    catch (Exception)
                    {
                        results.Add(new ResultVM()
                        {
                            PayoutId = payoutData.Id, Result = PayResult.Error, Destination = blob.Destination
                        });
                    }
                }
            }

            await ctx.SaveChangesAsync();
            return View("LightningPayoutResult", results);
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
}
