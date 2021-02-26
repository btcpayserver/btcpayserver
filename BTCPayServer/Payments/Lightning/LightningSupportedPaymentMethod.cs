using System;
using BTCPayServer.Lightning;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningSupportedPaymentMethod : ISupportedPaymentMethod
    {
        public const string InternalNode = "Internal Node";
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

        public LightningConnectionString GetExternalLightningUrl()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return GetLightningUrl();
#pragma warning restore CS0618 // Type or member is obsolete
        }
        [Obsolete("TODO")]
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
            else if (LightningChargeUrl != null)
            {
                var fullUri = new UriBuilder(LightningChargeUrl) { UserName = Username, Password = Password }.Uri.AbsoluteUri;
                if (!BTCPayServer.Lightning.LightningConnectionString.TryParse(fullUri, true, out var connectionString, out var error))
                {
                    throw new FormatException(error);
                }
                return connectionString;
            }
            else
                return null;
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

        public void SetInternalNode()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            LightningConnectionString = null;
            Username = null;
            Password = null;
            LightningChargeUrl = null;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [JsonIgnore]
        public bool IsInternalNode
        {
            get
            {
#pragma warning disable CS0618 // Type or member is obsolete
                return GetLightningUrl() is null;
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }
    }
}
