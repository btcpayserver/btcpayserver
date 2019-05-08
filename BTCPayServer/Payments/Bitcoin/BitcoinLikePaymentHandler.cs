using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;

namespace BTCPayServer.Payments.Bitcoin
{
    public class BitcoinLikePaymentHandler : PaymentMethodHandlerBase<DerivationSchemeSettings>
    {
        ExplorerClientProvider _ExplorerProvider;
        private IFeeProviderFactory _FeeRateProviderFactory;
        private Services.Wallets.BTCPayWalletProvider _WalletProvider;

        public BitcoinLikePaymentHandler(ExplorerClientProvider provider,
                                         IFeeProviderFactory feeRateProviderFactory,
                                         Services.Wallets.BTCPayWalletProvider walletProvider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));
            _ExplorerProvider = provider;
            this._FeeRateProviderFactory = feeRateProviderFactory;
            _WalletProvider = walletProvider;
        }

        class Prepare
        {
            public Task<FeeRate> GetFeeRate;
            public Task<BitcoinAddress> ReserveAddress;
        }

        public override object PreparePayment(DerivationSchemeSettings supportedPaymentMethod, StoreData store, BTCPayNetwork network)
        {
            return new Prepare()
            {
                GetFeeRate = _FeeRateProviderFactory.CreateFeeProvider(network).GetFeeRateAsync(),
                ReserveAddress = _WalletProvider.GetWallet(network).ReserveAddressAsync(supportedPaymentMethod.AccountDerivation)
            };
        }

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(DerivationSchemeSettings supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store, BTCPayNetwork network, object preparePaymentObject)
        {
            if (!_ExplorerProvider.IsAvailable(network))
                throw new PaymentMethodUnavailableException($"Full node not available");
            var prepare = (Prepare)preparePaymentObject;
            Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod onchainMethod = new Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod();
            onchainMethod.NetworkFeeMode = store.GetStoreBlob().NetworkFeeMode;
            onchainMethod.FeeRate = await prepare.GetFeeRate;
            switch (onchainMethod.NetworkFeeMode)
            {
                case NetworkFeeMode.Always:
                    onchainMethod.NextNetworkFee = onchainMethod.FeeRate.GetFee(100); // assume price for 100 bytes
                    break;
                case NetworkFeeMode.Never:
                case NetworkFeeMode.MultiplePaymentsOnly:
                    onchainMethod.NextNetworkFee = Money.Zero;
                    break;
            }
            onchainMethod.DepositAddress = (await prepare.ReserveAddress).ToString();
            return onchainMethod;
        }
    }
}
