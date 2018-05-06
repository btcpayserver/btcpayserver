using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Payments.Ethereum
{
    public class EthereumLikePaymentHandler : PaymentMethodHandlerBase<DerivationStrategy>
    {
        private Services.Wallets.BTCPayWalletProvider _WalletProvider;

        private IFeeProviderFactory _FeeRateProviderFactory;
  
        public EthereumLikePaymentHandler(IFeeProviderFactory feeRateProviderFactory,
            Services.Wallets.BTCPayWalletProvider walletProvider)
        {
            this._FeeRateProviderFactory = feeRateProviderFactory;
            _WalletProvider = walletProvider;
        }

        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(DerivationStrategy supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store, BTCPayNetwork network)
        {
            var getFeeRate = _FeeRateProviderFactory.CreateFeeProvider(network).GetFeeRateAsync();
            var getAddress = _WalletProvider.GetWallet(network).ReserveAddressAsync(supportedPaymentMethod.DerivationStrategyBase);
            EthereumLikePaymentMethod onchainMethod = new EthereumLikePaymentMethod();
            onchainMethod.FeeRate = await getFeeRate;
            onchainMethod.TxFee = onchainMethod.FeeRate.GetFee(100); // assume price for 100 bytes
            onchainMethod.DepositAddress = (await getAddress).ToString();
            return onchainMethod;
        }
    }
}
