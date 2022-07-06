#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace BTCPayServer.Payments.Lightning
{
    public class LNURLPayPaymentHandler : PaymentMethodHandlerBase<LNURLPaySupportedPaymentMethod, BTCPayNetwork>
    {
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly LightningLikePaymentHandler _lightningLikePaymentHandler;

        public LNURLPayPaymentHandler(
            BTCPayNetworkProvider networkProvider,
            CurrencyNameTable currencyNameTable,
            IOptions<LightningNetworkOptions> options,
            LightningLikePaymentHandler lightningLikePaymentHandler)
        {
            _networkProvider = networkProvider;
            _currencyNameTable = currencyNameTable;
            _lightningLikePaymentHandler = lightningLikePaymentHandler;
            Options = options;
        }

        public override PaymentType PaymentType => PaymentTypes.LightningLike;

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
            var cryptoInfo = invoiceResponse.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(model.CryptoCode);
            model.PaymentMethodName = GetPaymentMethodName(network);
            model.InvoiceBitcoinUrl = cryptoInfo.PaymentUrls?.AdditionalData["LNURLP"].ToObject<string>();
            model.InvoiceBitcoinUrlQR = model.InvoiceBitcoinUrl;
            model.BtcAddress = model.InvoiceBitcoinUrl;
            model.PeerInfo = ((LNURLPayPaymentMethodDetails)paymentMethod.GetPaymentMethodDetails()).NodeInfo;
            if (storeBlob.LightningAmountInSatoshi && model.CryptoCode == "BTC")
            {
                var satoshiCulture = new CultureInfo(CultureInfo.InvariantCulture.Name);
                satoshiCulture.NumberFormat.NumberGroupSeparator = " ";
                model.CryptoCode = "Sats";
                model.BtcDue = Money.Parse(model.BtcDue).ToUnit(MoneyUnit.Satoshi).ToString("N0", satoshiCulture);
                model.BtcPaid = Money.Parse(model.BtcPaid).ToUnit(MoneyUnit.Satoshi).ToString("N0", satoshiCulture);
                model.OrderAmount = Money.Parse(model.OrderAmount).ToUnit(MoneyUnit.Satoshi)
                    .ToString("N0", satoshiCulture);
                model.NetworkFee = new Money(model.NetworkFee, MoneyUnit.BTC).ToUnit(MoneyUnit.Satoshi);
                model.Rate =
                    _currencyNameTable.DisplayFormatCurrency(paymentMethod.Rate / 100_000_000, model.InvoiceCurrency);
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
