using System;
using BTCPayServer.Lightning;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningPaymentMethodConfig
    {
        public const string InternalNode = "Internal Node";
#nullable enable
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string? ConnectionString { get; set; }

        // Null preserves the behavior of configurations saved before endpoint safety was recorded.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? AllowUnsafeConnection { get; set; }

        public string? GetExternalLightningUrl()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (string.IsNullOrEmpty(ConnectionString))
                return null;
            return ConnectionString;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public void SetLightningUrl(ILightningClient client)
        {
            ArgumentNullException.ThrowIfNull(client);
#pragma warning disable CS0618 // Type or member is obsolete
            ConnectionString = client.ToString();
            InternalNodeRef = null;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public string GetDisplayableConnectionString()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (!string.IsNullOrEmpty(ConnectionString))
                return ConnectionString;
#pragma warning restore CS0618 // Type or member is obsolete
            if (InternalNodeRef is string s)
                return s;
            return "Invalid connection string";
        }

        public void SetInternalNode()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            ConnectionString = null;
            InternalNodeRef = InternalNode;
            AllowUnsafeConnection = null;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [JsonProperty]
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
