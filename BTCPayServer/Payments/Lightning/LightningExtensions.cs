using BTCPayServer.Configuration;
using BTCPayServer.Lightning;
using BTCPayServer.Services;

namespace BTCPayServer.Payments.Lightning
{
    public static class LightningExtensions
    {


        public static ILightningClient CreateLightningClient(this LightningSupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network, LightningNetworkOptions options, LightningClientFactoryService lightningClientFactory)
        {
            var external = supportedPaymentMethod.GetExternalLightningUrl();
            if (external != null)
            {
                return lightningClientFactory.Create(external, network);
            }
            else
            {
                if (!options.InternalLightningByCryptoCode.TryGetValue(network.CryptoCode, out var connectionString))
                    throw new PaymentMethodUnavailableException("No internal node configured");
                return connectionString;
            }
        }
    }
}
