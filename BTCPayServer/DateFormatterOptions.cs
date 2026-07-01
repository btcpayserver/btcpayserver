using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer;

/// <summary>
/// Options passed in JavaScript side to <see href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Intl/DateTimeFormat/DateTimeFormat"/>
/// </summary>
public class DateFormatterOptions
{
    public const string DefaultTemplateName = "Default";

    public static IReadOnlyList<Template> DateTemplates { get; } =
    [
        new("Short", "6/12/26", Parse("""
                                      {
                                        "year": "numeric",
                                        "month": "numeric",
                                        "day": "numeric"
                                      }
                                      """)),
        new("Medium", "Jun 12, 2026", Parse("""
                                            {
                                              "year": "numeric",
                                              "month": "short",
                                              "day": "numeric"
                                            }
                                            """)),
        new("Long", "June 12, 2026", Parse("""
                                           {
                                             "year": "numeric",
                                             "month": "long",
                                             "day": "numeric"
                                           }
                                           """))
    ];

    public static DateFormatterOptions Parse(string json)
        => JsonConvert.DeserializeObject<DateFormatterOptions>(json);

    public static Template GetTemplate(string name) =>
        DateTemplates.FirstOrDefault(o => o.Name == name);

    [JsonProperty("locales", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Locales { get; set; }

    [JsonProperty("dateStyle", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string DateStyle { get; set; }

    [JsonProperty("timeStyle", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string TimeStyle { get; set; }

    [JsonProperty("timeZone", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string TimeZone { get; set; }

    [JsonProperty("calendar", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Calendar { get; set; }

    [JsonProperty("numberingSystem", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string NumberingSystem { get; set; }

    [JsonProperty("localeMatcher", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string LocaleMatcher { get; set; }

    [JsonProperty("weekday", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Weekday { get; set; }

    [JsonProperty("era", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Era { get; set; }

    [JsonProperty("year", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Year { get; set; }

    [JsonProperty("month", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Month { get; set; }

    [JsonProperty("day", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Day { get; set; }

    [JsonProperty("dayPeriod", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string DayPeriod { get; set; }

    [JsonProperty("hour", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Hour { get; set; }

    [JsonProperty("minute", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Minute { get; set; }

    [JsonProperty("second", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Second { get; set; }

    [JsonProperty("fractionalSecondDigits", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public int? FractionalSecondDigits { get; set; }

    [JsonProperty("timeZoneName", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string TimeZoneName { get; set; }

    [JsonProperty("hour12", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public bool? Hour12 { get; set; }

    [JsonProperty("hourCycle", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string HourCycle { get; set; }

    [JsonProperty("formatMatcher", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string FormatMatcher { get; set; }

    public record Template(string Name, string Preview, DateFormatterOptions DateFormatOptions);

    public JObject ToJson()
        => JObject.FromObject(this);

    public DateFormatterOptions Merge(DateFormatterOptions otherOptions)
    {
        var templateJson = otherOptions.ToJson();
        var thisJson = ToJson();
        thisJson.Merge(templateJson, new JsonMergeSettings { MergeNullValueHandling = MergeNullValueHandling.Ignore });
        return thisJson.ToObject<DateFormatterOptions>();
    }
}
