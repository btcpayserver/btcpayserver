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
    public class BitcoinLikePaymentHandler : PaymentMethodHandlerBase<DerivationStrategy>
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

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(DerivationStrategy supportedPaymentMethod, PaymentMethod paymentMethod, BTCPayNetwork network)
        {
            var getFeeRate = _FeeRateProviderFactory.CreateFeeProvider(network).GetFeeRateAsync();
            var getAddress = _WalletProvider.GetWallet(network).ReserveAddressAsync(supportedPaymentMethod.DerivationStrategyBase);
            Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod onchainMethod = new Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod();
            onchainMethod.FeeRate = await getFeeRate;
            onchainMethod.TxFee = onchainMethod.FeeRate.GetFee(100); // assume price for 100 bytes
            onchainMethod.DepositAddress = await getAddress;
            return onchainMethod;
        }

        public override Task<bool> IsAvailable(DerivationStrategy supportedPaymentMethod, BTCPayNetwork network)
        {
            return Task.FromResult(_ExplorerProvider.IsAvailable(network));
        }
    }
}
