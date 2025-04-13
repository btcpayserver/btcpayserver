using BTCPayServer.Configuration;
using BTCPayServer.Lightning;
using BTCPayServer.Services;

namespace BTCPayServer.Payments.Lightning
{
    public static class LightningExtensions
    {

        public static bool IsConfigured(this LightningPaymentMethodConfig supportedPaymentMethod, BTCPayNetwork network, LightningNetworkOptions options)
        {
            return supportedPaymentMethod.GetExternalLightningUrl() is not null || (supportedPaymentMethod.IsInternalNode && options.InternalLightningByCryptoCode.ContainsKey(network.CryptoCode));
        }
        public static ILightningClient CreateLightningClient(this LightningPaymentMethodConfig supportedPaymentMethod, BTCPayNetwork network, LightningNetworkOptions options, LightningClientFactoryService lightningClientFactory)
        {
            var external = supportedPaymentMethod.GetExternalLightningUrl();
            if (external != null)
            {
                return lightningClientFactory.Create(external, network);
            }
            else
            {
                if (!supportedPaymentMethod.IsInternalNode || !options.InternalLightningByCryptoCode.TryGetValue(network.CryptoCode, out var connectionString))
                    throw new PaymentMethodUnavailableException("No internal node configured");
                return connectionString;
            }
        }
    }
}
