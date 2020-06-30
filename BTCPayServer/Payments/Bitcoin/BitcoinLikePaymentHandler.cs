using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;
using NBXplorer.Models;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Payments.Bitcoin
{
    public class BitcoinLikePaymentHandler : PaymentMethodHandlerBase<DerivationSchemeSettings, BTCPayNetwork>
    {
        readonly ExplorerClientProvider _ExplorerProvider;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly IFeeProviderFactory _FeeRateProviderFactory;
        private readonly NBXplorerDashboard _dashboard;
        private readonly Services.Wallets.BTCPayWalletProvider _WalletProvider;

        public BitcoinLikePaymentHandler(ExplorerClientProvider provider,
            BTCPayNetworkProvider networkProvider,
            IFeeProviderFactory feeRateProviderFactory,
            NBXplorerDashboard dashboard,
            Services.Wallets.BTCPayWalletProvider walletProvider)
        {
            _ExplorerProvider = provider;
            _networkProvider = networkProvider;
            _FeeRateProviderFactory = feeRateProviderFactory;
            _dashboard = dashboard;
            _WalletProvider = walletProvider;
        }

        class Prepare
        {
            public Task<FeeRate> GetFeeRate;
            public Task<FeeRate> GetNetworkFeeRate;
            public Task<KeyPathInformation> ReserveAddress;
        }

        public override void PreparePaymentModel(PaymentModel model, InvoiceResponse invoiceResponse,
            StoreBlob storeBlob)
        {
            var paymentMethodId = new PaymentMethodId(model.CryptoCode, PaymentTypes.BTCLike);

            var cryptoInfo = invoiceResponse.CryptoInfo.First(o => o.GetpaymentMethodId() == paymentMethodId);
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(model.CryptoCode);
            model.IsLightning = false;
            model.PaymentMethodName = GetPaymentMethodName(network);
            model.InvoiceBitcoinUrl = cryptoInfo.PaymentUrls.BIP21;
            model.InvoiceBitcoinUrlQR = cryptoInfo.PaymentUrls.BIP21;
        }

        public override string GetCryptoImage(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            return GetCryptoImage(network);
        }

        private string GetCryptoImage(BTCPayNetworkBase network)
        {
            return network.CryptoImagePath;
        }

        public override string GetPaymentMethodName(PaymentMethodId paymentMethodId)
        {
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            return GetPaymentMethodName(network);
        }

        public override async Task<string> IsPaymentMethodAllowedBasedOnInvoiceAmount(StoreBlob storeBlob,
            Dictionary<CurrencyPair, Task<RateResult>> rate, Money amount, PaymentMethodId paymentMethodId)
        {
            if (storeBlob.OnChainMinValue != null)
            {
                var currentRateToCrypto =
                    await rate[new CurrencyPair(paymentMethodId.CryptoCode, storeBlob.OnChainMinValue.Currency)];
                if (currentRateToCrypto?.BidAsk != null)
                {
                    var limitValueCrypto =
                        Money.Coins(storeBlob.OnChainMinValue.Value / currentRateToCrypto.BidAsk.Bid);
                    if (amount < limitValueCrypto)
                    {
                        return "The amount of the invoice is too low to be paid on chain";
                    }
                }
            }

            return string.Empty;
        }

        public override IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
        {
            return _networkProvider
                .GetAll()
                .OfType<BTCPayNetwork>()
                .Select(network => new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike));
        }

        private string GetPaymentMethodName(BTCPayNetworkBase network)
        {
            return network.DisplayName;
        }

        public override object PreparePayment(DerivationSchemeSettings supportedPaymentMethod, StoreData store,
            BTCPayNetworkBase network)
        {
            var storeBlob = store.GetStoreBlob();
            return new Prepare()
            {
                GetFeeRate =
                    _FeeRateProviderFactory.CreateFeeProvider(network)
                        .GetFeeRateAsync(storeBlob.RecommendedFeeBlockTarget),
                GetNetworkFeeRate = storeBlob.NetworkFeeMode == NetworkFeeMode.Never
                    ? null
                    : _FeeRateProviderFactory.CreateFeeProvider(network).GetFeeRateAsync(),
                ReserveAddress = _WalletProvider.GetWallet(network)
                    .ReserveAddressAsync(supportedPaymentMethod.AccountDerivation)
            };
        }

        public override PaymentType PaymentType => PaymentTypes.BTCLike;

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(
            InvoiceLogs logs,
            DerivationSchemeSettings supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store,
            BTCPayNetwork network, object preparePaymentObject)
        {
            if (!_ExplorerProvider.IsAvailable(network))
                throw new PaymentMethodUnavailableException($"Full node not available");
            var prepare = (Prepare)preparePaymentObject;
            Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod onchainMethod =
                new Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod();
            var blob = store.GetStoreBlob();
            onchainMethod.NetworkFeeMode = blob.NetworkFeeMode;
            onchainMethod.FeeRate = await prepare.GetFeeRate;
            switch (onchainMethod.NetworkFeeMode)
            {
                case NetworkFeeMode.Always:
                    onchainMethod.NetworkFeeRate = (await prepare.GetNetworkFeeRate);
                    onchainMethod.NextNetworkFee =
                        onchainMethod.NetworkFeeRate.GetFee(100); // assume price for 100 bytes
                    break;
                case NetworkFeeMode.Never:
                    onchainMethod.NetworkFeeRate = FeeRate.Zero;
                    onchainMethod.NextNetworkFee = Money.Zero;
                    break;
                case NetworkFeeMode.MultiplePaymentsOnly:
                    onchainMethod.NetworkFeeRate = (await prepare.GetNetworkFeeRate);
                    onchainMethod.NextNetworkFee = Money.Zero;
                    break;
            }

            onchainMethod.DepositAddress = (await prepare.ReserveAddress).Address.ToString();
            onchainMethod.PayjoinEnabled = blob.PayJoinEnabled &&
                                           PayjoinClient.SupportedFormats.Contains(supportedPaymentMethod
                                               .AccountDerivation.ScriptPubKeyType()) &&
                                           network.SupportPayJoin;
            if (onchainMethod.PayjoinEnabled)
            {
                var prefix = $"{supportedPaymentMethod.PaymentId.ToPrettyString()}:";
                var nodeSupport = _dashboard?.Get(network.CryptoCode)?.Status?.BitcoinStatus?.Capabilities
                    ?.CanSupportTransactionCheck is true;
                onchainMethod.PayjoinEnabled &= supportedPaymentMethod.IsHotWallet && nodeSupport;
                if (!supportedPaymentMethod.IsHotWallet)
                    logs.Write($"{prefix} Payjoin should have been enabled, but your store is not a hotwallet");
                if (!nodeSupport)
                    logs.Write($"{prefix} Payjoin should have been enabled, but your version of NBXplorer or full node does not support it.");
                if (onchainMethod.PayjoinEnabled)
                    logs.Write($"{prefix} Payjoin is enabled for this invoice.");
            }

            return onchainMethod;
        }
    }
}
