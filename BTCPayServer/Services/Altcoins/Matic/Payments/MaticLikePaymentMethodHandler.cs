#if ALTCOINS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Services.Altcoins.Matic.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;

namespace BTCPayServer.Services.Altcoins.Matic.Payments
{
    public class
        MaticLikePaymentMethodHandler : PaymentMethodHandlerBase<MaticSupportedPaymentMethod,
            MaticBTCPayNetwork>
    {
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly MaticService _maticService;

        public MaticLikePaymentMethodHandler(BTCPayNetworkProvider networkProvider, MaticService maticService)
        {
            _networkProvider = networkProvider;
            _maticService = maticService;
        }

        public override PaymentType PaymentType => MaticPaymentType.Instance;

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(InvoiceLogs logs,
            MaticSupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod,
            StoreData store, MaticBTCPayNetwork network, object preparePaymentObject)
        {
            if (!_maticService.IsAvailable(network.CryptoCode, out var error))
                throw new PaymentMethodUnavailableException(error??$"Not configured yet");
            var invoice = paymentMethod.ParentEntity;
            if (!(preparePaymentObject is Prepare ethPrepare)) throw new ArgumentException();
            var address = await ethPrepare.ReserveAddress(invoice.Id);
            if (address is null || address.Failed)
            {
                throw new PaymentMethodUnavailableException($"could not generate address");
            }
            
            return new MaticLikeOnChainPaymentMethodDetails()
            {
                DepositAddress = address.Address, Index = address.Index, XPub = address.XPub
            };
        }

        public override object PreparePayment(MaticSupportedPaymentMethod supportedPaymentMethod, StoreData store,
            BTCPayNetworkBase network)
        {
            return new Prepare()
            {
                ReserveAddress = s =>
                    _maticService.ReserveNextAddress(
                        new MaticService.ReserveMaticAddress()
                        {
                            StoreId = store.Id, CryptoCode = network.CryptoCode
                        })
            };
        }

        class Prepare
        {
            public Func<string, Task<MaticService.ReserveMaticAddressResponse>> ReserveAddress;
        }

        public override void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse,
            StoreBlob storeBlob)
        {
            var paymentMethodId = new PaymentMethodId(model.CryptoCode, PaymentType);
            var cryptoInfo = invoiceResponse.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
            var network = _networkProvider.GetNetwork<MaticBTCPayNetwork>(model.CryptoCode);
            model.IsLightning = false;
            model.PaymentMethodName = GetPaymentMethodName(network);
            model.CryptoImage = GetCryptoImage(network);
            model.InvoiceBitcoinUrl = "";
            model.InvoiceBitcoinUrlQR = cryptoInfo.Address;
        }

        public override string GetCryptoImage(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<MaticBTCPayNetwork>(paymentMethodId.CryptoCode);
            return GetCryptoImage(network);
        }

        public override string GetPaymentMethodName(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<MaticBTCPayNetwork>(paymentMethodId.CryptoCode);
            return GetPaymentMethodName(network);
        }

        public override IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
        {
            return _networkProvider.GetAll().OfType<MaticBTCPayNetwork>()
                .Select(network => new PaymentMethodId(network.CryptoCode, PaymentType));
        }

        public override CheckoutUIPaymentMethodSettings GetCheckoutUISettings()
        {
            return new CheckoutUIPaymentMethodSettings()
            {
                ExtensionPartial = "Matic/MaticLikeMethodCheckout",
                CheckoutBodyVueComponentName = "MaticLikeMethodCheckout",
                CheckoutHeaderVueComponentName = "MaticLikeMethodCheckoutHeader",
                NoScriptPartialName = "Bitcoin_Lightning_LikeMethodCheckoutNoScript"
            };
        }

        private string GetCryptoImage(MaticBTCPayNetwork network)
        {
            return network.CryptoImagePath;
        }


        private string GetPaymentMethodName(MaticBTCPayNetwork network)
        {
            return $"{network.DisplayName}";
        }
    }
}
#endif
