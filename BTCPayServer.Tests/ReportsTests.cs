using System;
using BTCPayServer.Client.Models;
using BTCPayServer.Components.DateRangeSelector;
using BTCPayServer.Models.StoreReportsViewModels;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Tests;

public class ReportsTests
{
    [Fact]
    public void AllTimeFilterIsPreservedForReports()
    {
        var model = new StoreReportsViewModel
        {
            FilterCommand = "alltime",
            SearchTerm = "timezone:UTC"
        };

        var search = model.GetSearch();

        Assert.Equal("alltime", search.GetFilterString("daterange"));
    }

    [Fact]
    public void RollingDateRangeEndsNow()
    {
        var before = DateTimeOffset.UtcNow;
        var range = new SearchString("daterange:-24h,timezone:UTC").GetDateRange(TimeZoneInfo.Utc);
        var after = DateTimeOffset.UtcNow;

        Assert.NotNull(range.StartDate);
        Assert.InRange(range.EndDate!.Value, before, after);
    }

    [Fact]
    public void AllTimeDateRangeStartsAtUnixEpoch()
    {
        var before = DateTimeOffset.UtcNow;
        var range = new SearchString("daterange:alltime,timezone:UTC").GetDateRange(TimeZoneInfo.Utc);
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(DateTimeOffset.UnixEpoch, range.StartDate);
        Assert.InRange(range.EndDate!.Value, before, after);
    }

    [Fact]
    public void DateRangeSelectorRecognizesAllTimeFilter()
    {
        var model = new DateRangeSelectorModel
        {
            Search = new SearchString("daterange:alltime,timezone:UTC"),
            CustomRangeTitle = "Custom range"
        };

        Assert.True(model.HasDateFilter);
        Assert.True(model.IsAllTime);
        Assert.False(model.HasCustomDateFilter);
    }

    [Fact]
    public void ReportBoundsAreSerializedAsUnixTimestamps()
    {
        var report = JObject.FromObject(new StoreReportResponse
        {
            From = DateTimeOffset.UnixEpoch,
            To = DateTimeOffset.UnixEpoch.AddMinutes(1)
        });

        Assert.Equal(0L, report["From"]?.Value<long>());
        Assert.Equal(60L, report["To"]?.Value<long>());
    }
}
