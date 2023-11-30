#nullable enable
using System;
using BTCPayServer.Lightning;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningSupportedPaymentMethod : ISupportedPaymentMethod
    {
        public const string InternalNode = "Internal Node";
        public string CryptoCode { get; set; } = string.Empty;

        [JsonIgnore]
        public PaymentMethodId PaymentId => new PaymentMethodId(CryptoCode, PaymentTypes.LightningLike);

        [Obsolete("Use Get/SetLightningUrl")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string? LightningConnectionString { get; set; }

        public string? GetExternalLightningUrl()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (string.IsNullOrEmpty(LightningConnectionString))
                return null;
            // if (!BTCPayServer.Lightning.LightningConnectionString.TryParse(LightningConnectionString, false, out var connectionString, out var error))
            // {
            //     throw new FormatException(error);
            // }
            return LightningConnectionString;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public void SetLightningUrl(ILightningClient connectionString)
        {
            ArgumentNullException.ThrowIfNull(connectionString);
#pragma warning disable CS0618 // Type or member is obsolete
            LightningConnectionString = connectionString.ToString();
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public string GetDisplayableConnectionString()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (!string.IsNullOrEmpty(LightningConnectionString))
                return LightningConnectionString;
#pragma warning restore CS0618 // Type or member is obsolete
            if (InternalNodeRef is string s)
                return s;
            return "Invalid connection string";
        }

        public void SetInternalNode()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            LightningConnectionString = null;
            InternalNodeRef = InternalNode;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string? InternalNodeRef { get; set; }
        [JsonIgnore]
        public bool IsInternalNode
        {
            get
            {
#pragma warning disable CS0618 // Type or member is obsolete
                return InternalNodeRef == InternalNode;
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }
    }
}
