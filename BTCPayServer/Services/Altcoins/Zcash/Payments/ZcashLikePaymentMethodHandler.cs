#if ALTCOINS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Rating;
using BTCPayServer.Services.Altcoins.Zcash.RPC.Models;
using BTCPayServer.Services.Altcoins.Zcash.Services;
using BTCPayServer.Services.Altcoins.Zcash.Utils;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;

namespace BTCPayServer.Services.Altcoins.Zcash.Payments
{
    public class ZcashLikePaymentMethodHandler : PaymentMethodHandlerBase<ZcashSupportedPaymentMethod, ZcashLikeSpecificBtcPayNetwork>
    {
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly ZcashRPCProvider _ZcashRpcProvider;

        public ZcashLikePaymentMethodHandler(BTCPayNetworkProvider networkProvider, ZcashRPCProvider ZcashRpcProvider)
        {
            _networkProvider = networkProvider;
            _ZcashRpcProvider = ZcashRpcProvider;
        }
        public override PaymentType PaymentType => ZcashPaymentType.Instance;

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(InvoiceLogs logs, ZcashSupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod,
            StoreData store, ZcashLikeSpecificBtcPayNetwork network, object preparePaymentObject, IEnumerable<PaymentMethodId> invoicePaymentMethods)
        {
            
            if (preparePaymentObject is null)
            {
                return new ZcashLikeOnChainPaymentMethodDetails()
                {
                    Activated = false
                };
            }

            if (!_ZcashRpcProvider.IsAvailable(network.CryptoCode))
                throw new PaymentMethodUnavailableException($"Node or wallet not available");
            var invoice = paymentMethod.ParentEntity;
            if (!(preparePaymentObject is Prepare ZcashPrepare))
                throw new ArgumentException();
            var feeRatePerKb = await ZcashPrepare.GetFeeRate;
            var address = await ZcashPrepare.ReserveAddress(invoice.Id);

            var feeRatePerByte = feeRatePerKb.Fee / 1024;
            return new ZcashLikeOnChainPaymentMethodDetails()
            {
                NextNetworkFee = ZcashMoney.Convert(feeRatePerByte * 100),
                AccountIndex = supportedPaymentMethod.AccountIndex,
                AddressIndex = address.AddressIndex,
                DepositAddress = address.Address,
                Activated = true
            };

        }

        public override object PreparePayment(ZcashSupportedPaymentMethod supportedPaymentMethod, StoreData store,
            BTCPayNetworkBase network)
        {

            var walletClient = _ZcashRpcProvider.WalletRpcClients[supportedPaymentMethod.CryptoCode];
            var daemonClient = _ZcashRpcProvider.DaemonRpcClients[supportedPaymentMethod.CryptoCode];
            return new Prepare()
            {
                GetFeeRate = daemonClient.SendCommandAsync<GetFeeEstimateRequest, GetFeeEstimateResponse>("get_fee_estimate", new GetFeeEstimateRequest()),
                ReserveAddress = s => walletClient.SendCommandAsync<CreateAddressRequest, CreateAddressResponse>("create_address", new CreateAddressRequest() { Label = $"btcpay invoice #{s}", AccountIndex = supportedPaymentMethod.AccountIndex })
            };
        }

        class Prepare
        {
            public Task<GetFeeEstimateResponse> GetFeeRate;
            public Func<string, Task<CreateAddressResponse>> ReserveAddress;
        }

        public override void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse,
            StoreBlob storeBlob, IPaymentMethod paymentMethod)
        {
            var paymentMethodId = paymentMethod.GetId();
            var network = _networkProvider.GetNetwork<ZcashLikeSpecificBtcPayNetwork>(model.CryptoCode);
            model.PaymentMethodName = GetPaymentMethodName(network);
            model.CryptoImage = GetCryptoImage(network);
            if (model.Activated)
            {
                var cryptoInfo = invoiceResponse.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
                model.InvoiceBitcoinUrl = ZcashPaymentType.Instance.GetPaymentLink(network, null,
                    new ZcashLikeOnChainPaymentMethodDetails() {DepositAddress = cryptoInfo.Address}, cryptoInfo.GetDue().Value,
                    null);
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
            var network = _networkProvider.GetNetwork<ZcashLikeSpecificBtcPayNetwork>(paymentMethodId.CryptoCode);
            return GetCryptoImage(network);
        }

        public override string GetPaymentMethodName(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<ZcashLikeSpecificBtcPayNetwork>(paymentMethodId.CryptoCode);
            return GetPaymentMethodName(network);
        }
        public override IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
        {
            return _networkProvider.GetAll()
                .Where(network => network is ZcashLikeSpecificBtcPayNetwork)
                .Select(network => new PaymentMethodId(network.CryptoCode, PaymentType));
        }

        private string GetCryptoImage(ZcashLikeSpecificBtcPayNetwork network)
        {
            return network.CryptoImagePath;
        }


        private string GetPaymentMethodName(ZcashLikeSpecificBtcPayNetwork network)
        {
            return $"{network.DisplayName}";
        }
    }
}
#endif
