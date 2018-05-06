using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Payments.Ethereum
{
    public class EthereumLikePaymentHandler : PaymentMethodHandlerBase<DerivationStrategy>
    {
        public override async Task<IPaymentMethodDetails> CreatePaymentMethodDetails(DerivationStrategy supportedPaymentMethod, PaymentMethod paymentMethod, StoreData store, BTCPayNetwork network)
        {
            var ethereumLikePaymentMethod = new EthereumLikePaymentMethod();
            return ethereumLikePaymentMethod;
        }
    }
}
