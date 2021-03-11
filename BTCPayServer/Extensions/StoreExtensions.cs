using System.Linq;
using BTCPayServer.Data;

namespace BTCPayServer
{
    public static class StoreExtensions
    {
        public static DerivationSchemeSettings GetDerivationSchemeSettings(this StoreData store, BTCPayNetworkProvider networkProvider, string cryptoCode)
        {
            var paymentMethod = store
                .GetSupportedPaymentMethods(networkProvider)
                .OfType<DerivationSchemeSettings>()
                .FirstOrDefault(p => p.PaymentId.PaymentType == Payments.PaymentTypes.BTCLike && p.PaymentId.CryptoCode == cryptoCode);
            return paymentMethod;
        }

    }
}
