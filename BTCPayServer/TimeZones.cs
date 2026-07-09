#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace BTCPayServer;

public static class TimeZones
{
    private static readonly Dictionary<string, string> abbreviations;
    private static readonly Dictionary<string, TimeZoneInfo> zones;

    static TimeZones()
    {
        zones = TimeZoneInfo.GetSystemTimeZones().ToDictionary(t => t.Id, t => t, StringComparer.InvariantCultureIgnoreCase);
        abbreviations = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            ["Etc/Unknown"] = "UTC",
            ["Etc/UTC"] = "UTC",
            ["Etc/GMT"] = "UTC",
            ["GMT"] = "UTC",
            ["JST"] = "Asia/Tokyo",
            ["KST"] = "Asia/Seoul",
            ["HKT"] = "Asia/Hong_Kong",
            ["SGT"] = "Asia/Singapore",
            ["PHT"] = "Asia/Manila",
            ["CET"] = "Europe/Paris",
            ["CEST"] = "Europe/Paris",
            ["EST"] = "America/New_York",
            ["EDT"] = "America/New_York",
            ["CDT"] = "America/Chicago",
            ["MDT"] = "America/Denver",
            ["PST"] = "America/Los_Angeles",
            ["PDT"] = "America/Los_Angeles",
            ["AKST"] = "America/Anchorage",
            ["AKDT"] = "America/Anchorage",
            ["HST"] = "Pacific/Honolulu"
        };
    }

    public static TimeZoneInfo? TryGet(string id)
        => TryGet(id, out var zone) ? zone : null;

    public static bool TryGet(string id, [MaybeNullWhen(false)] out TimeZoneInfo zone)
    {
        if (zones.TryGetValue(id, out zone))
            return true;
        abbreviations.TryGetValue(id, out var fullName);
        return zones.TryGetValue(fullName ?? id, out zone);
    }
}
