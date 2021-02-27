using System;
using System.Collections.Generic;
using BTCPayServer.Lightning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningSupportedPaymentMethod : ISupportedPaymentMethod
    {
        public const string InternalNode = "Internal Node";
        public string CryptoCode { get; set; }

        [JsonIgnore]
        public PaymentMethodId PaymentId => new PaymentMethodId(CryptoCode, PaymentTypes.LightningLike);

        [Obsolete("Use Get/SetLightningUrl")]
        public string LightningConnectionString { get; set; }

        public LightningConnectionString GetExternalLightningUrl()
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
                return null;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public void SetLightningUrl(LightningConnectionString connectionString)
        {
            if (connectionString == null)
                throw new ArgumentNullException(nameof(connectionString));
#pragma warning disable CS0618 // Type or member is obsolete
            LightningConnectionString = connectionString.ToString();
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public void SetInternalNode()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            LightningConnectionString = null;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [JsonIgnore]
        public bool IsInternalNode
        {
            get
            {
#pragma warning disable CS0618 // Type or member is obsolete
                return GetExternalLightningUrl() is null;
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }
    }
}
