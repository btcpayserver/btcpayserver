#if ALTCOINS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Common.Altcoins.Chia.RPC.Models;
using BTCPayServer.Common.Altcoins.Chia.Utils;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Services.Altcoins.Chia.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;

namespace BTCPayServer.Services.Altcoins.Chia.Payments
{
    public class
        ChiaLikePaymentMethodHandler : PaymentMethodHandlerBase<ChiaSupportedPaymentMethod,
            ChiaLikeSpecificBtcPayNetwork>
    {
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly ChiaRPCProvider _ChiaRpcProvider;

        public ChiaLikePaymentMethodHandler(BTCPayNetworkProvider networkProvider, ChiaRPCProvider ChiaRpcProvider)
        {
            _networkProvider = networkProvider;
            _ChiaRpcProvider = ChiaRpcProvider;
        }

        public override PaymentType PaymentType => ChiaPaymentType.Instance;

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(InvoiceLogs logs,
            ChiaSupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod,
            StoreData store, ChiaLikeSpecificBtcPayNetwork network, object preparePaymentObject,
            IEnumerable<PaymentMethodId> invoicePaymentMethods)
        {
            if (preparePaymentObject is null)
            {
                return new ChiaLikeOnChainPaymentMethodDetails() { Activated = false };
            }

            if (!_ChiaRpcProvider.IsAvailable(network.CryptoCode))
                throw new PaymentMethodUnavailableException($"Node or wallet not available");
            var invoice = paymentMethod.ParentEntity;
            if (!(preparePaymentObject is Prepare ChiaPrepare))
                throw new ArgumentException();
            var address = await ChiaPrepare.ReserveAddress(invoice.Id);

            return new ChiaLikeOnChainPaymentMethodDetails()
            {
                WalletId = supportedPaymentMethod.WalletId,
                DepositAddress = address.Address,
                Activated = true
            };
        }

        public override object PreparePayment(ChiaSupportedPaymentMethod supportedPaymentMethod, StoreData store,
            BTCPayNetworkBase network)
        {
            var walletClient = _ChiaRpcProvider.WalletRpcClients[supportedPaymentMethod.CryptoCode];
            return new Prepare()
            {
                ReserveAddress = s =>
                    walletClient.SendCommandAsync<GetNextAddressRequest, GetNextAddressResponse>("get_next_address",
                        new GetNextAddressRequest() { WalletId = supportedPaymentMethod.WalletId, NewAddress = true })
            };
        }

        class Prepare
        {
            public Func<string, Task<GetNextAddressResponse>> ReserveAddress;
        }

        public override void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse,
            StoreBlob storeBlob, IPaymentMethod paymentMethod)
        {
            var paymentMethodId = paymentMethod.GetId();
            var network = _networkProvider.GetNetwork<ChiaLikeSpecificBtcPayNetwork>(model.CryptoCode);
            model.PaymentMethodName = GetPaymentMethodName(network);
            model.CryptoImage = GetCryptoImage(network);
            if (model.Activated)
            {
                var cryptoInfo = invoiceResponse.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
                // Enable when Chia has a standard for payment links
                model.InvoiceBitcoinUrl = ChiaPaymentType.Instance.GetPaymentLink(network, null,
                    new ChiaLikeOnChainPaymentMethodDetails() { DepositAddress = cryptoInfo.Address },
                    cryptoInfo.GetDue().Value,
                    null);
                // model.InvoiceBitcoinUrl = cryptoInfo.Address;
                model.InvoiceBitcoinUrlQR = model.InvoiceBitcoinUrl;
            }
            else
            {
                model.InvoiceBitcoinUrl = "";
                model.InvoiceBitcoinUrlQR = "";
            }
        }

        public override string GetCryptoImage(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<ChiaLikeSpecificBtcPayNetwork>(paymentMethodId.CryptoCode);
            return GetCryptoImage(network);
        }

        public override string GetPaymentMethodName(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<ChiaLikeSpecificBtcPayNetwork>(paymentMethodId.CryptoCode);
            return GetPaymentMethodName(network);
        }

        public override IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
        {
            return _networkProvider.GetAll()
                .Where(network => network is ChiaLikeSpecificBtcPayNetwork)
                .Select(network => new PaymentMethodId(network.CryptoCode, PaymentType));
        }

        private string GetCryptoImage(ChiaLikeSpecificBtcPayNetwork network)
        {
            return network.CryptoImagePath;
        }


        private string GetPaymentMethodName(ChiaLikeSpecificBtcPayNetwork network)
        {
            return $"{network.DisplayName}";
        }
    }
}
#endif
