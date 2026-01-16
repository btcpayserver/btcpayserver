#nullable enable
using BTCPayServer.Configuration;
using BTCPayServer.Lightning;
using BTCPayServer.Services;
using NBitcoin;

namespace BTCPayServer.Payments.Lightning
{
    public static class LightningExtensions
    {
        public static bool IsConfigured(this LightningPaymentMethodConfig supportedPaymentMethod, BTCPayNetwork network, LightningNetworkOptions options)
        {
            return supportedPaymentMethod.GetExternalLightningUrl() is not null ||
                   (supportedPaymentMethod.IsInternalNode && options.InternalLightningByCryptoCode.ContainsKey(network.CryptoCode));
        }

        public static ILightningClient CreateLightningClient(this LightningPaymentMethodConfig supportedPaymentMethod, BTCPayNetwork network,
            LightningNetworkOptions options, LightningClientFactoryService lightningClientFactory)
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
        public static uint256? GetPaymentHash(this LightningInvoice lightningInvoice, Network btcpayNetwork)
        {
            return lightningInvoice.PaymentHash != null ?
                uint256.Parse(lightningInvoice.PaymentHash) :
                BOLT11PaymentRequest.Parse(lightningInvoice.BOLT11, btcpayNetwork).PaymentHash;
        }
    }
}
