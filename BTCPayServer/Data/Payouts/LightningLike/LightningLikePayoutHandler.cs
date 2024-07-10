using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using LNURL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;

namespace BTCPayServer.Data.Payouts.LightningLike
{
    public class LightningLikePayoutHandler : IPayoutHandler
    {
        public const string LightningLikePayoutHandlerOnionNamedClient =
            nameof(LightningLikePayoutHandlerOnionNamedClient);

        public const string LightningLikePayoutHandlerClearnetNamedClient =
            nameof(LightningLikePayoutHandlerClearnetNamedClient);

        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly UserService _userService;
        private readonly IAuthorizationService _authorizationService;

        public LightningLikePayoutHandler(BTCPayNetworkProvider btcPayNetworkProvider,
            IHttpClientFactory httpClientFactory, UserService userService, IAuthorizationService authorizationService)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _httpClientFactory = httpClientFactory;
            _userService = userService;
            _authorizationService = authorizationService;
        }

        public bool CanHandle(PaymentMethodId paymentMethod)
        {
            return (paymentMethod.PaymentType == LightningPaymentType.Instance || paymentMethod.PaymentType == LNURLPayPaymentType.Instance) &&
                   _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethod.CryptoCode)?.SupportLightning is true;
        }

        public Task TrackClaim(ClaimRequest claimRequest, PayoutData payoutData)
        {
            return Task.CompletedTask;
        }

        public HttpClient CreateClient(Uri uri)
        {
            return _httpClientFactory.CreateClient(uri.IsOnion()
                ? LightningLikePayoutHandlerOnionNamedClient
                : LightningLikePayoutHandlerClearnetNamedClient);
        }

        public async Task<(IClaimDestination destination, string error)> ParseClaimDestination(PaymentMethodId paymentMethodId, string destination, CancellationToken cancellationToken)
        {
            destination = destination.Trim();
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            try
            {
                string lnurlTag = null;
                var lnurl = destination.IsValidEmail()
                    ? LNURL.LNURL.ExtractUriFromInternetIdentifier(destination)
                    : LNURL.LNURL.Parse(destination, out lnurlTag);

                if (lnurlTag is null)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
                    var rawInfo = await LNURL.LNURL.FetchInformation(lnurl, CreateClient(lnurl), t.Token);
                    if(rawInfo is null)
                        return (null, "The LNURL / Lightning Address provided was not online.");
                    if(rawInfo is not LNURLPayRequest info)
                        return (null, "The LNURL was not a valid LNURL Pay request.");
                    lnurlTag = info.Tag;
                }

                if (lnurlTag.Equals("payRequest", StringComparison.InvariantCultureIgnoreCase))
                {
                    return (new LNURLPayClaimDestinaton(destination), null);
                }
            }
            catch (FormatException)
            {
            }
            catch
            {
                return (null, "The LNURL / Lightning Address provided was not online.");
            }

            var result =
                BOLT11PaymentRequest.TryParse(destination, out var invoice, network.NBitcoinNetwork)
                    ? new BoltInvoiceClaimDestination(destination, invoice)
                    : null;

            if (result == null)
                return (null, "A valid BOLT11 invoice or LNURL Pay or Lightning address was not provided.");
            if (invoice.ExpiryDate.UtcDateTime < DateTime.UtcNow)
            {
                return (null,
                    "The BOLT11 invoice submitted has expired.");
            }

            return (result, null);
        }

        public (bool valid, string error) ValidateClaimDestination(IClaimDestination claimDestination, PullPaymentBlob pullPaymentBlob)
        {
            if (claimDestination is not BoltInvoiceClaimDestination bolt)
                return (true, null);
            var invoice = bolt.PaymentRequest;
            if (pullPaymentBlob is not null && (invoice.ExpiryDate.UtcDateTime - DateTime.UtcNow) < pullPaymentBlob.BOLT11Expiration)
            {
                return (false,
                    $"The BOLT11 invoice must have an expiry date of at least {(long)pullPaymentBlob.BOLT11Expiration.TotalDays} days from submission (Provided was only {(invoice.ExpiryDate.UtcDateTime - DateTime.UtcNow).Days}).");
            }
            return (true, null);
        }

        public IPayoutProof ParseProof(PayoutData payout)
        {
            BitcoinLikePayoutHandler.ParseProofType(payout.Proof, out var raw, out var proofType);
            if (proofType is null)
            {
                return null;
            }
            if (proofType == PayoutLightningBlob.PayoutLightningBlobProofType)
            {
                return raw.ToObject<PayoutLightningBlob>();
            }

            return raw.ToObject<ManualPayoutProof>();
        }

        public void StartBackgroundCheck(Action<Type[]> subscribe)
        {
        }

        public Task BackgroundCheck(object o)
        {
            return Task.CompletedTask;
        }

        public async Task<decimal> GetMinimumPayoutAmount(PaymentMethodId paymentMethod, IClaimDestination claimDestination)
        {
            if(claimDestination is LNURLPayClaimDestinaton lnurlPayClaimDestinaton)
            {
                try
                {
                    var lnurl = lnurlPayClaimDestinaton.LNURL.IsValidEmail()
                        ? LNURL.LNURL.ExtractUriFromInternetIdentifier(lnurlPayClaimDestinaton.LNURL)
                        : LNURL.LNURL.Parse(lnurlPayClaimDestinaton.LNURL, out var lnurlTag);

                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var rawInfo = await LNURL.LNURL.FetchInformation(lnurl, CreateClient(lnurl), timeout.Token);
                    if (rawInfo is LNURLPayRequest info)
                        return info.MinSendable.ToDecimal(LightMoneyUnit.BTC);
                }
                catch
                {
                    // ignored
                }
            }
            return Money.Satoshis(1).ToDecimal(MoneyUnit.BTC);
        }

        public Dictionary<PayoutState, List<(string Action, string Text)>> GetPayoutSpecificActions()
        {
            return new Dictionary<PayoutState, List<(string Action, string Text)>>();
        }

        public Task<StatusMessageModel> DoSpecificAction(string action, string[] payoutIds, string storeId)
        {
            return Task.FromResult<StatusMessageModel>(null);
        }

        public async Task<IEnumerable<PaymentMethodId>> GetSupportedPaymentMethods(StoreData storeData)
        {
            var result = new List<PaymentMethodId>();
            var methods = storeData.GetEnabledPaymentMethods(_btcPayNetworkProvider).Where(id => id.PaymentId.PaymentType == LightningPaymentType.Instance).OfType<LightningSupportedPaymentMethod>();
            foreach (LightningSupportedPaymentMethod supportedPaymentMethod in methods)
            {
                if (!supportedPaymentMethod.IsInternalNode)
                {
                    result.Add(supportedPaymentMethod.PaymentId);
                    continue;
                }

                foreach (UserStore storeDataUserStore in storeData.UserStores)
                {
                    if (!await _userService.IsAdminUser(storeDataUserStore.ApplicationUserId))
                        continue;
                    result.Add(supportedPaymentMethod.PaymentId);
                    break;
                }

            }

            return result;
        }

        public Task<IActionResult> InitiatePayment(PaymentMethodId paymentMethodId, string[] payoutIds)
        {
            return Task.FromResult<IActionResult>(new RedirectToActionResult("ConfirmLightningPayout",
                "UILightningLikePayout", new { cryptoCode = paymentMethodId.CryptoCode, payoutIds }));
        }

    }
}
