using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Monero.RPC.Models
{
    public partial class Info
    {
        [JsonProperty("address")] public string Address { get; set; }
        [JsonProperty("avg_download")] public long AvgDownload { get; set; }
        [JsonProperty("avg_upload")] public long AvgUpload { get; set; }
        [JsonProperty("connection_id")] public string ConnectionId { get; set; }
        [JsonProperty("current_download")] public long CurrentDownload { get; set; }
        [JsonProperty("current_upload")] public long CurrentUpload { get; set; }
        [JsonProperty("height")] public long Height { get; set; }
        [JsonProperty("host")] public string Host { get; set; }
        [JsonProperty("incoming")] public bool Incoming { get; set; }
        [JsonProperty("ip")] public string Ip { get; set; }
        [JsonProperty("live_time")] public long LiveTime { get; set; }
        [JsonProperty("local_ip")] public bool LocalIp { get; set; }
        [JsonProperty("localhost")] public bool Localhost { get; set; }
        [JsonProperty("peer_id")] public string PeerId { get; set; }

        [JsonProperty("port")]
        [JsonConverter(typeof(ParseStringConverter))]
        public long Port { get; set; }

        [JsonProperty("recv_count")] public long RecvCount { get; set; }
        [JsonProperty("recv_idle_time")] public long RecvIdleTime { get; set; }
        [JsonProperty("send_count")] public long SendCount { get; set; }
        [JsonProperty("send_idle_time")] public long SendIdleTime { get; set; }
        [JsonProperty("state")] public string State { get; set; }
        [JsonProperty("support_flags")] public long SupportFlags { get; set; }
    }
}
