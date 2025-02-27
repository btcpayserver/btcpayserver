using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

public class EmailSettingsData
{
    public string Server { get; set; }
    public int? Port { get; set; }
    public string Login { get; set; }
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Password { get; set; }
    public bool? PasswordSet { get; set; }
    public string From { get; set; }
    public bool DisableCertificateCheck { get; set; }
    [JsonExtensionData]
    public IDictionary<string, JToken> AdditionalData { get; set; } = new Dictionary<string, JToken>();
}
