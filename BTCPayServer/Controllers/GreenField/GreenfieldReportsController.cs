#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers.GreenField;

[ApiController]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
[EnableCors(CorsPolicies.All)]
public class GreenfieldReportsController(ReportService reportService) : ControllerBase
{
    public ReportService ReportService { get; } = reportService;

    public const string DefaultReport = "Invoices";

    [Authorize(Policy = Policies.CanViewReports, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [HttpGet("~/api/v1/stores/{storeId}/reports")]
    public ActionResult<IEnumerable<string>> GetStoreReports(string storeId)
    {
        return Ok(ReportService.ReportProviders.Values
            .Where(report => report.IsAvailable())
            .Select(report => report.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
    }

    [Authorize(Policy = Policies.CanViewReports, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [HttpPost("~/api/v1/stores/{storeId}/reports")]
    public async Task<IActionResult> StoreReports(string storeId, StoreReportRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request?.Search))
        {
            ModelState.AddModelError(nameof(StoreReportRequest.Search), "The search field is required.");
            return this.CreateValidationError(ModelState);
        }

        var search = new SearchString(request.Search);
        var viewName = search.GetFilterString("view");
        if (string.IsNullOrWhiteSpace(viewName))
        {
            ModelState.AddModelError(nameof(StoreReportRequest.Search), "The view filter is required.");
            return this.CreateValidationError(ModelState);
        }

        var dateRange = search.GetFilterString("daterange");
        var startDate = search.GetFilterString("startdate");
        var endDate = search.GetFilterString("enddate");
        if (dateRange is null && startDate is null)
        {
            ModelState.AddModelError(nameof(StoreReportRequest.Search), "A daterange or startdate filter is required.");
            return this.CreateValidationError(ModelState);
        }
        if (dateRange is not null && !SearchString.IsValidDateRange(dateRange))
        {
            ModelState.AddModelError(nameof(StoreReportRequest.Search), "The daterange filter is invalid.");
            return this.CreateValidationError(ModelState);
        }

        var timeZoneId = search.GetExplicitTimeZone();
        TimeZoneInfo? timeZone = null;
        if (timeZoneId is not null && !TimeZones.TryGet(timeZoneId, out timeZone))
        {
            ModelState.AddModelError(nameof(StoreReportRequest.Search), "The timezone filter is invalid.");
            return this.CreateValidationError(ModelState);
        }

        var requiresTimeZone = dateRange is not null ||
                               startDate is not null && !HasExplicitOffset(startDate) ||
                               endDate is not null && !HasExplicitOffset(endDate);
        if (requiresTimeZone && timeZone is null)
        {
            ModelState.AddModelError(nameof(StoreReportRequest.Search), "A valid timezone filter is required for this date range.");
            return this.CreateValidationError(ModelState);
        }

        var parsedRange = search.GetDateRange(timeZone ?? TimeZoneInfo.Utc);
        if ((dateRange != "alltime" && parsedRange.StartDate is null) ||
            (endDate is not null && parsedRange.EndDate is null))
        {
            ModelState.AddModelError(nameof(StoreReportRequest.Search), "The date range is invalid.");
            return this.CreateValidationError(ModelState);
        }

        var range = ReportService.ResolveRange(search, timeZone ?? TimeZoneInfo.Utc, DateTimeOffset.UtcNow);
        if (range.From >= range.To)
        {
            ModelState.AddModelError(nameof(StoreReportRequest.Search), "The start date must be before the end date.");
            return this.CreateValidationError(ModelState);
        }

        var result = await Query(storeId, viewName, range.From, range.To, cancellationToken);
        if (result is not ObjectResult { Value: StoreReportResponse reportResponse })
            return result;

        return Ok(ToApiResponse(reportResponse));
    }

    [NonAction]
    public async Task<IActionResult> StoreReports(string storeId, SearchString? search = null,
        CancellationToken cancellationToken = default)
    {
        search ??= new SearchString(null);
        var viewName = search.GetFilterString("view") ?? DefaultReport;
        var range = ReportService.ResolveRange(search, TimeZoneInfo.Utc, DateTimeOffset.UtcNow);
        return await Query(storeId, viewName, range.From, range.To, cancellationToken);
    }

    private async Task<IActionResult> Query(string storeId, string viewName, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (!ReportService.ReportProviders.TryGetValue(viewName, out var report))
        {
            ModelState.AddModelError("view", "View doesn't exist");
            return this.CreateValidationError(ModelState);
        }
        if (!report.IsAvailable())
            return this.CreateAPIError(503, "view-unavailable", "This view is unavailable at this moment");

        return Ok(await ReportService.Query(storeId, report, from, to, cancellationToken));
    }

    private static StoreReportResponse ToApiResponse(StoreReportResponse response)
    {
        var rows = response.Data.Select(row => new JArray(row.Select((value, index) =>
            NormalizeValue(value, index < response.Fields.Count ? response.Fields[index].Type : null)))).ToList();
        return new StoreReportResponse
        {
            ReportName = response.ReportName,
            From = response.From,
            To = response.To,
            Fields = response.Fields,
            Data = rows
        };
    }

    private static JToken? NormalizeValue(JToken? value, string? fieldType)
    {
        if (string.Equals(fieldType, "datetime", StringComparison.OrdinalIgnoreCase) && value is JValue date)
        {
            if (date.Value is DateTimeOffset dateTimeOffset)
                return dateTimeOffset.ToUnixTimeSeconds();
            if (date.Value is DateTime dateTime)
                return new DateTimeOffset(dateTime).ToUnixTimeSeconds();
        }
        if (string.Equals(fieldType, "amount", StringComparison.OrdinalIgnoreCase) &&
            value is JObject amount &&
            decimal.TryParse(amount["v"]?.Value<string>(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) &&
            amount["d"]?.Value<int>() is { } divisibility)
        {
            return number.ToString($"F{divisibility}", CultureInfo.InvariantCulture);
        }
        if (value is JValue { Value: decimal decimalValue })
            return decimalValue.ToString(CultureInfo.InvariantCulture);
        return value?.DeepClone();
    }

    private static bool HasExplicitOffset(string value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _) &&
               Regex.IsMatch(value, "(?:Z|[+-]\\d{2}:\\d{2})$", RegexOptions.IgnoreCase);
    }
}
