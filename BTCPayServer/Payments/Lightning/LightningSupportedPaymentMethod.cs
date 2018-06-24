using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningSupportedPaymentMethod : ISupportedPaymentMethod
    {
        public string CryptoCode { get; set; }
        // This property MUST be after CryptoCode or else JSON serialization fails
        public PaymentMethodId PaymentId => new PaymentMethodId(CryptoCode, PaymentTypes.LightningLike);

        [Obsolete("Use Get/SetLightningUrl")]
        public string LightningChargeUrl { get; set; }

        [Obsolete("Use Get/SetLightningUrl")]
        public string Username { get; set; }
        [Obsolete("Use Get/SetLightningUrl")]
        public string Password { get; set; }

        [Obsolete("Use Get/SetLightningUrl")]
        public string Macaroon { get; set; }
        [Obsolete("Use Get/SetLightningUrl")]
        public string Tls { get; set; }

        public LightningConnectionString GetLightningUrl()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var fullUri = new UriBuilder(LightningChargeUrl) { UserName = Username, Password = Password }.Uri.AbsoluteUri;
            if(!LightningConnectionString.TryParse(fullUri, out var connectionString, out var error))
            {
                throw new FormatException(error);
            }
            connectionString.Macaroon = Macaroon;
            connectionString.Tls = Tls;
            return connectionString;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public void SetLightningUrl(LightningConnectionString connectionString)
        {
            if (connectionString == null)
                throw new ArgumentNullException(nameof(connectionString));

#pragma warning disable CS0618 // Type or member is obsolete
            Username = connectionString.Username;
            Password = connectionString.Password;
            Macaroon = connectionString.Macaroon;
            Tls = connectionString.Tls;

            LightningChargeUrl = connectionString.BaseUri.AbsoluteUri;
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }
}
