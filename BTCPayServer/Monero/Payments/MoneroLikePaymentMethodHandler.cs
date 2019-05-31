using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Monero.RPC.Models;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;
using NBitpayClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.Monero
{
    public class MoneroLikePaymentMethodHandler : PaymentMethodHandlerBase<MoneroSupportedPaymentMethod, MoneroLikeSpecificBtcPayNetwork>
    {
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly MoneroRPCProvider _moneroRpcProvider;

        public MoneroLikePaymentMethodHandler(BTCPayNetworkProvider networkProvider, MoneroRPCProvider moneroRpcProvider)
        {
            _networkProvider = networkProvider;
            _moneroRpcProvider = moneroRpcProvider;
        }


        public override string PrettyDescription => string.Empty;
        public override PaymentTypes PaymentType => PaymentTypes.MoneroLike;

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(MoneroSupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod,
            StoreData store, MoneroLikeSpecificBtcPayNetwork network, object preparePaymentObject)
        {
            var invoice = paymentMethod.ParentEntity;
            var client = _moneroRpcProvider.WalletRpcClients [supportedPaymentMethod.CryptoCode];
            var response  = await client.CreateAddress(new CreateAddressRequest() {Label = $"btcpay invoice #{invoice.Id}", AccountIndex = supportedPaymentMethod.AccountIndex });

            return new MoneroLikeOnChainPaymentMethodDetails()
            {
                AccountIndex = supportedPaymentMethod.AccountIndex,
                AddressIndex = response.AddressIndex,
                DepositAddress = response.Address
            };
        }

        public override void PrepareInvoiceDto(InvoiceResponse invoiceResponse, InvoiceEntity invoiceEntity,
            InvoiceCryptoInfo invoiceCryptoInfo, PaymentMethodAccounting accounting, PaymentMethod info)
        {
        }

        public override void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse)
        {
            var paymentMethodId = new PaymentMethodId(model.CryptoCode, PaymentTypes.MoneroLike);

            var cryptoInfo = invoiceResponse.CryptoInfo.First(o => Extensions.GetpaymentMethodId(o) == paymentMethodId);
            var network = _networkProvider.GetNetwork<MoneroLikeSpecificBtcPayNetwork>(model.CryptoCode);
            model.IsLightning = false;
            model.PaymentMethodName = GetPaymentMethodName(network);
            model.CryptoImage = GetCryptoImage(network);
            model.InvoiceBitcoinUrl = cryptoInfo.PaymentUrls.BOLT11;
            model.InvoiceBitcoinUrlQR = cryptoInfo.PaymentUrls.BOLT11.ToUpperInvariant();
        }

        public override string GetCryptoImage(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<MoneroLikeSpecificBtcPayNetwork>(paymentMethodId.CryptoCode);
            return GetCryptoImage(network);
        }

        public override string GetPaymentMethodName(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<MoneroLikeSpecificBtcPayNetwork>(paymentMethodId.CryptoCode);
            return GetPaymentMethodName(network);
        }

        public override Task<string> IsPaymentMethodAllowedBasedOnInvoiceAmount(StoreBlob storeBlob, Dictionary<CurrencyPair, Task<RateResult>> rate, Money amount,
            PaymentMethodId paymentMethodId)
        {
            return Task.FromResult<string>(null);
        }

        public override IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
        {
            return _networkProvider.GetAll()
                .Where(network => network is MoneroLikeSpecificBtcPayNetwork)
                .Select(network => new PaymentMethodId(network.CryptoCode, PaymentTypes.MoneroLike));
        }

        public override CryptoPaymentData GetCryptoPaymentData(PaymentEntity paymentEntity)
        {
#pragma warning disable CS0618
            return JsonConvert.DeserializeObject<MoneroLikePaymentData>(paymentEntity.CryptoPaymentData);
#pragma warning restore CS0618
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(PaymentMethodId paymentMethodId, JToken value)
        {
            throw new System.NotImplementedException();
        }

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(JObject jobj)
        {
            throw new System.NotImplementedException();
        }

        public override string GetTransactionLink(PaymentMethodId paymentMethodId, params object[] args)
        {
            return string.Format(
                _networkProvider.GetNetwork<MoneroLikeSpecificBtcPayNetwork>(paymentMethodId.CryptoCode)
                    .BlockExplorerLink, args);
        }


        private string GetCryptoImage(BTCPayNetwork network)
        {
            return network.CryptoImagePath;
        }


        private string GetPaymentMethodName(BTCPayNetwork network)
        {
            return $"{network.DisplayName}";
        }
    }
}
