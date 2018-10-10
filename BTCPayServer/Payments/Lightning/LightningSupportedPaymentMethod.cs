using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using NBitcoin;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningSupportedPaymentMethod : ISupportedPaymentMethod
    {
        public string CryptoCode { get; set; }

        [Obsolete("Use Get/SetLightningUrl")]
        public string Username { get; set; }
        [Obsolete("Use Get/SetLightningUrl")]
        public string Password { get; set; }

        // This property MUST be after CryptoCode or else JSON serialization fails
        public PaymentMethodId PaymentId => new PaymentMethodId(CryptoCode, PaymentTypes.LightningLike);

        [Obsolete("Use Get/SetLightningUrl")]
        public string LightningChargeUrl { get; set; }

        [Obsolete("Use Get/SetLightningUrl")]
        public string LightningConnectionString { get; set; }

        public LightningConnectionString GetLightningUrl()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (!string.IsNullOrEmpty(LightningConnectionString))
            {
                if (!BTCPayServer.Lightning.LightningConnectionString.TryParse(LightningConnectionString, false, out var connectionString, out var error))
                {
                    throw new FormatException(error);
                }
                return connectionString;
            }
            else
            {
                var fullUri = new UriBuilder(LightningChargeUrl) { UserName = Username, Password = Password }.Uri.AbsoluteUri;
                if (!BTCPayServer.Lightning.LightningConnectionString.TryParse(fullUri, true, out var connectionString, out var error))
                {
                    throw new FormatException(error);
                }
                return connectionString;
            }
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public void SetLightningUrl(LightningConnectionString connectionString)
        {
            if (connectionString == null)
                throw new ArgumentNullException(nameof(connectionString));

#pragma warning disable CS0618 // Type or member is obsolete
            LightningConnectionString = connectionString.ToString();
            Username = null;
            Password = null;
            LightningChargeUrl = null;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public ILightningClient CreateClient(BTCPayNetwork network)
        {
            return LightningClientFactory.CreateClient(this.GetLightningUrl(), network.NBitcoinNetwork);
        }
    }
}
