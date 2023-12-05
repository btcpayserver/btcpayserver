using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public enum OnExistingBehavior
    {
        KeepVersion,
        UpdateVersion
    }
    public class RegisterBoltcardRequest
    {
        [JsonConverter(typeof(HexJsonConverter))]
        [JsonProperty("UID")]
        public byte[] UID { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public OnExistingBehavior? OnExisting { get; set; }
    }
    public class RegisterBoltcardResponse
    {
        [JsonProperty("LNURLW")]
        public string LNURLW { get; set; }
        public int Version { get; set; }
        [JsonProperty("K0")]
        public string K0 { get; set; }
        [JsonProperty("K1")]
        public string K1 { get; set; }
        [JsonProperty("K2")]
        public string K2 { get; set; }
        [JsonProperty("K3")]
        public string K3 { get; set; }
        [JsonProperty("K4")]
        public string K4 { get; set; }
    }
}
