using System;
using BTCPayServer.Models.StoreReportsViewModels;
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
}
