using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Services.Altcoins.Monero.RPC.Models;
using BTCPayServer.Services.Altcoins.Monero.Services;
using BTCPayServer.Services.Altcoins.Monero.Utils;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;

namespace BTCPayServer.Services.Altcoins.Monero.Payments
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
        public override PaymentType PaymentType => MoneroPaymentType.Instance;

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(MoneroSupportedPaymentMethod supportedPaymentMethod, PaymentMethod paymentMethod,
            StoreData store, MoneroLikeSpecificBtcPayNetwork network, object preparePaymentObject)
        {
            
            if (!_moneroRpcProvider.IsAvailable(network.CryptoCode))
                throw new PaymentMethodUnavailableException($"Node or wallet not available");
            var invoice = paymentMethod.ParentEntity;
            if (!(preparePaymentObject is Prepare moneroPrepare)) throw new ArgumentException();
            var feeRatePerKb = await moneroPrepare.GetFeeRate;
            var address = await moneroPrepare.ReserveAddress(invoice.Id);

            var feeRatePerByte = feeRatePerKb.Fee / 1024;
            return new MoneroLikeOnChainPaymentMethodDetails()
            {
                NextNetworkFee = MoneroMoney.Convert(feeRatePerByte * 100),
                AccountIndex = supportedPaymentMethod.AccountIndex,
                AddressIndex = address.AddressIndex,
                DepositAddress = address.Address
            };

        }

        public override object PreparePayment(MoneroSupportedPaymentMethod supportedPaymentMethod, StoreData store,
            BTCPayNetworkBase network)
        {
            
            var walletClient = _moneroRpcProvider.WalletRpcClients [supportedPaymentMethod.CryptoCode];
            var daemonClient = _moneroRpcProvider.DaemonRpcClients [supportedPaymentMethod.CryptoCode];
            return new Prepare()
            {
                GetFeeRate = daemonClient.SendCommandAsync<GetFeeEstimateRequest, GetFeeEstimateResponse>("get_fee_estimate", new GetFeeEstimateRequest()),
                ReserveAddress = s =>  walletClient. SendCommandAsync<CreateAddressRequest, CreateAddressResponse>("create_address", new CreateAddressRequest() {Label = $"btcpay invoice #{s}", AccountIndex = supportedPaymentMethod.AccountIndex })
            };
        }
        
        class Prepare
        {
            public Task<GetFeeEstimateResponse> GetFeeRate;
            public Func<string, Task<CreateAddressResponse>> ReserveAddress;
        }

        public override void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse, StoreBlob storeBlob)
        {
            var paymentMethodId = new PaymentMethodId(model.CryptoCode, PaymentType);

            var client = _moneroRpcProvider.WalletRpcClients[model.CryptoCode];
            
            var cryptoInfo = invoiceResponse.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
            var network = _networkProvider.GetNetwork<MoneroLikeSpecificBtcPayNetwork>(model.CryptoCode);
            model.IsLightning = false;
            model.PaymentMethodName = GetPaymentMethodName(network);
            model.CryptoImage = GetCryptoImage(network);
            model.InvoiceBitcoinUrl = client.SendCommandAsync<MakeUriRequest, MakeUriResponse>("make_uri", new MakeUriRequest()
                {
                    Address = cryptoInfo.Address,
                    Amount = MoneroMoney.Convert(decimal.Parse(cryptoInfo.Due, CultureInfo.InvariantCulture))
                }).GetAwaiter()
                .GetResult().Uri;
            model.InvoiceBitcoinUrlQR = model.InvoiceBitcoinUrl;
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
                .Select(network => new PaymentMethodId(network.CryptoCode, PaymentType));
        }

        private string GetCryptoImage(MoneroLikeSpecificBtcPayNetwork network)
        {
            return network.CryptoImagePath;
        }


        private string GetPaymentMethodName(MoneroLikeSpecificBtcPayNetwork network)
        {
            return $"{network.DisplayName}";
        }
    }
}
