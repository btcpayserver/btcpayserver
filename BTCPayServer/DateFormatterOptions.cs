using Newtonsoft.Json;

namespace BTCPayServer;

/// <summary>
/// Options passed in JavaScript side to <see href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Intl/DateTimeFormat/DateTimeFormat"/>
/// </summary>
public class DateFormatterOptions
{
    [JsonProperty("locales", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Locales { get; set; }
    [JsonProperty("dateStyle", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string DateStyle { get; set; }
    [JsonProperty("timeStyle", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string TimeStyle { get; set; }
    [JsonProperty("timeZone", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string TimeZone { get; set; }
}
