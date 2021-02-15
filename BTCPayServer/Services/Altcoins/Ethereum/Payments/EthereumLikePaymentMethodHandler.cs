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
using BTCPayServer.Services.Altcoins.Ethereum.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;

namespace BTCPayServer.Services.Altcoins.Ethereum.Payments
{
    public class
        EthereumLikePaymentMethodHandler : PaymentMethodHandlerBase<EthereumSupportedPaymentMethod,
            EthereumBTCPayNetwork>
    {
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly EthereumService _ethereumService;

        public EthereumLikePaymentMethodHandler(BTCPayNetworkProvider networkProvider, EthereumService ethereumService)
        {
            _networkProvider = networkProvider;
            _ethereumService = ethereumService;
        }

        public override PaymentType PaymentType => EthereumPaymentType.Instance;

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(InvoiceLogs logs,
            EthereumSupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod,
            StoreData store, EthereumBTCPayNetwork network, object preparePaymentObject)
        {
            if (preparePaymentObject is null)
            {
                return new EthereumLikeOnChainPaymentMethodDetails()
                {
                    Activated = false
                };
            }
            if (!_ethereumService.IsAvailable(network.CryptoCode, out var error))
                throw new PaymentMethodUnavailableException(error??$"Not configured yet");
            var invoice = paymentMethod.ParentEntity;
            if (!(preparePaymentObject is Prepare ethPrepare)) throw new ArgumentException();
            var address = await ethPrepare.ReserveAddress(invoice.Id);
            if (address is null || address.Failed)
            {
                throw new PaymentMethodUnavailableException($"could not generate address");
            }
            
            return new EthereumLikeOnChainPaymentMethodDetails()
            {
                DepositAddress = address.Address, Index = address.Index, XPub = address.XPub, Activated = true
            };
        }

        public override object PreparePayment(EthereumSupportedPaymentMethod supportedPaymentMethod, StoreData store,
            BTCPayNetworkBase network)
        {
            return new Prepare()
            {
                ReserveAddress = s =>
                    _ethereumService.ReserveNextAddress(
                        new EthereumService.ReserveEthereumAddress()
                        {
                            StoreId = store.Id, CryptoCode = network.CryptoCode
                        })
            };
        }

        class Prepare
        {
            public Func<string, Task<EthereumService.ReserveEthereumAddressResponse>> ReserveAddress;
        }

        public override void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse,
            StoreBlob storeBlob, IPaymentMethod paymentMethod)
        {
            var paymentMethodId = paymentMethod.GetId();
            var cryptoInfo = invoiceResponse.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
            var network = _networkProvider.GetNetwork<EthereumBTCPayNetwork>(model.CryptoCode);
            model.PaymentMethodName = GetPaymentMethodName(network);
            model.CryptoImage = GetCryptoImage(network);
            model.InvoiceBitcoinUrl = "";
            model.InvoiceBitcoinUrlQR = cryptoInfo.Address ?? "";
        }

        public override string GetCryptoImage(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<EthereumBTCPayNetwork>(paymentMethodId.CryptoCode);
            return GetCryptoImage(network);
        }

        public override string GetPaymentMethodName(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<EthereumBTCPayNetwork>(paymentMethodId.CryptoCode);
            return GetPaymentMethodName(network);
        }

        public override IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
        {
            return _networkProvider.GetAll().OfType<EthereumBTCPayNetwork>()
                .Select(network => new PaymentMethodId(network.CryptoCode, PaymentType));
        }

        public override CheckoutUIPaymentMethodSettings GetCheckoutUISettings()
        {
            return new CheckoutUIPaymentMethodSettings()
            {
                ExtensionPartial = "Ethereum/EthereumLikeMethodCheckout",
                CheckoutBodyVueComponentName = "EthereumLikeMethodCheckout",
                CheckoutHeaderVueComponentName = "EthereumLikeMethodCheckoutHeader",
                NoScriptPartialName = "Bitcoin/BitcoinLikeMethodCheckoutNoScript"
            };
        }

        private string GetCryptoImage(EthereumBTCPayNetwork network)
        {
            return network.CryptoImagePath;
        }


        private string GetPaymentMethodName(EthereumBTCPayNetwork network)
        {
            return $"{network.DisplayName}";
        }
    }
}
#endif
