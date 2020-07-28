#if ALTCOINS_RELEASE || DEBUG
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
                DepositAddress = address.Address, Index = address.Index, XPub = address.XPub
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
            StoreBlob storeBlob)
        {
            var paymentMethodId = new PaymentMethodId(model.CryptoCode, PaymentType);
            var cryptoInfo = invoiceResponse.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
            var network = _networkProvider.GetNetwork<EthereumBTCPayNetwork>(model.CryptoCode);
            model.IsLightning = false;
            model.PaymentMethodName = GetPaymentMethodName(network);
            model.CryptoImage = GetCryptoImage(network);
            model.InvoiceBitcoinUrl = "";
            model.InvoiceBitcoinUrlQR = cryptoInfo.Address;
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

        public override Task<string> IsPaymentMethodAllowedBasedOnInvoiceAmount(StoreBlob storeBlob,
            Dictionary<CurrencyPair, Task<RateResult>> rate, Money amount,
            PaymentMethodId paymentMethodId)
        {
            return Task.FromResult<string>(null);
        }

        public override IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
        {
            return _networkProvider.GetAll().OfType<EthereumBTCPayNetwork>()
                .Select(network => new PaymentMethodId(network.CryptoCode, PaymentType));
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
