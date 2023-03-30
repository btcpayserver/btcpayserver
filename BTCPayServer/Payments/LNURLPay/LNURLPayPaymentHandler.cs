#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Payments.Lightning
{
    public class LNURLPayPaymentHandler : PaymentMethodHandlerBase<LNURLPaySupportedPaymentMethod, BTCPayNetwork>
    {
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly DisplayFormatter _displayFormatter;
        private readonly LightningLikePaymentHandler _lightningLikePaymentHandler;
        private readonly LightningClientFactoryService _lightningClientFactoryService;

        public LNURLPayPaymentHandler(
            BTCPayNetworkProvider networkProvider,
            DisplayFormatter displayFormatter,
            IOptions<LightningNetworkOptions> options,
            LightningLikePaymentHandler lightningLikePaymentHandler,
            LightningClientFactoryService lightningClientFactoryService)
        {
            _networkProvider = networkProvider;
            _displayFormatter = displayFormatter;
            _lightningLikePaymentHandler = lightningLikePaymentHandler;
            _lightningClientFactoryService = lightningClientFactoryService;
            Options = options;
        }

        public override PaymentType PaymentType => PaymentTypes.LightningLike;

        private const string UriScheme = "lightning:";
        
        public IOptions<LightningNetworkOptions> Options { get; }

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(
            InvoiceLogs logs,
            LNURLPaySupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod, Data.StoreData store,
            BTCPayNetwork network, object preparePaymentObject, IEnumerable<PaymentMethodId> invoicePaymentMethods)
        {
            var lnPmi = new PaymentMethodId(supportedPaymentMethod.CryptoCode, PaymentTypes.LightningLike);
            if (!supportedPaymentMethod.EnableForStandardInvoices &&
                paymentMethod.ParentEntity.Type == InvoiceType.Standard &&
                invoicePaymentMethods.Contains(lnPmi))
            {
                throw new PaymentMethodUnavailableException("LNURL is not enabled for standard invoices");
            }
            if (string.IsNullOrEmpty(paymentMethod.ParentEntity.Id))
            {
                var lnSupported = store.GetSupportedPaymentMethods(_networkProvider)
                    .OfType<LightningSupportedPaymentMethod>().SingleOrDefault(method =>
                        method.PaymentId.CryptoCode == supportedPaymentMethod.CryptoCode &&
                        method.PaymentId.PaymentType == LightningPaymentType.Instance);

                if (lnSupported is null)
                {
                    throw new PaymentMethodUnavailableException("LNURL requires a lightning node to be configured for the store.");
                }
                using var cts = new CancellationTokenSource(LightningLikePaymentHandler.LightningTimeout);
                try
                {
                    var client = lnSupported.CreateLightningClient(network, Options.Value, _lightningClientFactoryService);
                    await client.GetInfo(cts.Token);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw new PaymentMethodUnavailableException("The lightning node did not reply in a timely manner");
                }

                return new LNURLPayPaymentMethodDetails()
                {
                    Activated = false,
                    LightningSupportedPaymentMethod = lnSupported
                };
            }


            var lnLightningSupportedPaymentMethod =
                ((LNURLPayPaymentMethodDetails)paymentMethod.GetPaymentMethodDetails()).LightningSupportedPaymentMethod;

            NodeInfo? nodeInfo = null;
            if (lnLightningSupportedPaymentMethod != null)
            {
                nodeInfo = (await _lightningLikePaymentHandler.GetNodeInfo(lnLightningSupportedPaymentMethod, _networkProvider.GetNetwork<BTCPayNetwork>(supportedPaymentMethod.CryptoCode), logs, paymentMethod.PreferOnion)).FirstOrDefault();
            }

            return new LNURLPayPaymentMethodDetails
            {
                Activated = true,
                LightningSupportedPaymentMethod = lnLightningSupportedPaymentMethod,
                BTCPayInvoiceId = paymentMethod.ParentEntity.Id,
                Bech32Mode = supportedPaymentMethod.UseBech32Scheme,
                NodeInfo = nodeInfo?.ToString()
            };
        }

        public override IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
        {
            return _networkProvider
                .GetAll()
                .OfType<BTCPayNetwork>()
                .Where(network => network.NBitcoinNetwork.Consensus.SupportSegwit && network.SupportLightning)
                .Select(network => new PaymentMethodId(network.CryptoCode, PaymentTypes.LNURLPay));
        }

        public override void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse,
            StoreBlob storeBlob, IPaymentMethod paymentMethod)
        {
            var paymentMethodId = paymentMethod.GetId();
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(model.CryptoCode);
            var cryptoInfo = invoiceResponse.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
            var lnurl = cryptoInfo.PaymentUrls?.AdditionalData["LNURLP"].ToObject<string>();
            
            model.PaymentMethodName = GetPaymentMethodName(network);
            model.BtcAddress = lnurl?.Replace(UriScheme, "");
            model.InvoiceBitcoinUrl = lnurl;
            model.InvoiceBitcoinUrlQR = lnurl?.ToUpperInvariant().Replace(UriScheme.ToUpperInvariant(), UriScheme);
            model.PeerInfo = ((LNURLPayPaymentMethodDetails)paymentMethod.GetPaymentMethodDetails()).NodeInfo;
            
            if (storeBlob.LightningAmountInSatoshi && model.CryptoCode == "BTC")
            {
                base.PreparePaymentModelForAmountInSats(model, paymentMethod, _displayFormatter);
            }
        }

        public override string GetCryptoImage(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            return GetCryptoImage(network);
        }

        private string GetCryptoImage(BTCPayNetworkBase network)
        {
            return ((BTCPayNetwork)network).LightningImagePath;
        }

        public override string GetPaymentMethodName(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            return GetPaymentMethodName(network);
        }

        public override CheckoutUIPaymentMethodSettings GetCheckoutUISettings()
        {
            return new CheckoutUIPaymentMethodSettings()
            {
                ExtensionPartial = "Lightning/LightningLikeMethodCheckout",
                CheckoutBodyVueComponentName = "LightningLikeMethodCheckout",
                CheckoutHeaderVueComponentName = "LightningLikeMethodCheckoutHeader",
                NoScriptPartialName = "Lightning/LightningLikeMethodCheckoutNoScript"
            };
        }

        private string GetPaymentMethodName(BTCPayNetworkBase network)
        {
            return $"{network.DisplayName} (Lightning LNURL)";
        }

        public override object PreparePayment(LNURLPaySupportedPaymentMethod supportedPaymentMethod,
            Data.StoreData store,
            BTCPayNetworkBase network)
        {
            // pass a non null obj, so that if lazy payment feature is used, it has a marker to trigger activation
            return new { };
        }
    }
}
