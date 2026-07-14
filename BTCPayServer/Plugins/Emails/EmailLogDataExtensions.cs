using System;
using BTCPayServer.Data;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Emails;

public enum EmailLogStatus { Sent, Failed }

public class EmailLogBlob
{
    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public EmailLogStatus Status { get; set; }
    public string Trigger { get; set; }
    public string[] To { get; set; } 
    public string[] CC { get; set; } 
    public string[] BCC { get; set; } 
    public string Subject { get; set; } 
    public string Body { get; set; } 
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string? Error { get; set; }
}

#nullable enable
public static class EmailLogDataExtensions
{
    public static EmailLogBlob? GetBlob(this EmailLogData log) => log.Blob is null ? null : JsonConvert.DeserializeObject<EmailLogBlob>(log.Blob);

    public static void SetBlob(this EmailLogData log, EmailLogBlob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        log.Blob = JsonConvert.SerializeObject(blob);
    }
}
#nullable restore
