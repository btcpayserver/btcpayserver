using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Models
{
    public class TokenRequest
    {
        [JsonProperty(PropertyName = "id")]
        public string Id
        {
            get; set;
        }

        [JsonProperty(PropertyName = "guid")]
        public string Guid
        {
            get; set;
        }
        [JsonProperty(PropertyName = "facade")]
        public string Facade
        {
            get; set;
        }
        [JsonProperty(PropertyName = "count")]
        public int Count
        {
            get; set;
        }
        [JsonProperty(PropertyName = "label")]
        public string Label
        {
            get; set;
        }

        [JsonProperty(PropertyName = "pairingCode")]
        public string PairingCode
        {
            get; set;
        }
    }

    public class PairingCodeResponse
    {
        [JsonProperty(PropertyName = "policies")]
        public JArray Policies { get; set; }

        [JsonProperty(PropertyName = "pairingCode")]
        public string PairingCode
        {
            get; set;
        }

        [JsonProperty(PropertyName = "pairingExpiration")]
        [JsonConverter(typeof(DateTimeJsonConverter))]
        public DateTimeOffset PairingExpiration
        {
            get; set;
        }

        [JsonProperty(PropertyName = "dateCreated")]
        [JsonConverter(typeof(DateTimeJsonConverter))]
        public DateTimeOffset DateCreated
        {
            get; set;
        }

        [JsonProperty(PropertyName = "facade")]
        public string Facade
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "token")]
        public string Token
        {
            get;
            set;
        }

        [JsonProperty(PropertyName = "label")]
        public string Label
        {
            get;
            set;
        }
    }
}
