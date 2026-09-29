using System;
using BTCPayServer.Client.Models;
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
        var range = new SearchString("daterange:-24h,timezone:UTC").GetDateRange(TimeZoneInfo.Utc);

        Assert.NotNull(range.StartDate);
        Assert.Null(range.EndDate);
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
