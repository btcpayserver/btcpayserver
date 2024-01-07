#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Authorization;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Services;
using System.Linq;
using System.Threading;

namespace BTCPayServer.Controllers.GreenField;

[ApiController]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
[EnableCors(CorsPolicies.All)]
public class GreenfieldReportsController : Controller
{
    public GreenfieldReportsController(
        ApplicationDbContextFactory dbContextFactory,
        ReportService reportService)
    {
        DBContextFactory = dbContextFactory;
        ReportService = reportService;
    }
    public ApplicationDbContextFactory DBContextFactory { get; }
    public ReportService ReportService { get; }

    [Authorize(Policy = Policies.CanViewReports, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [HttpPost("~/api/v1/stores/{storeId}/reports")]
    [NonAction] // Disabling this endpoint as we still need to figure out the request/response model
    public async Task<IActionResult> StoreReports(string storeId, [FromBody] StoreReportRequest? vm = null, CancellationToken cancellationToken = default)
    {
        vm ??= new StoreReportRequest();
        vm.ViewName ??= "Payments";
        vm.TimePeriod ??= new TimePeriod();
        vm.TimePeriod.To ??= DateTime.UtcNow;
        vm.TimePeriod.From ??= vm.TimePeriod.To.Value.AddMonths(-1);
        var from = vm.TimePeriod.From.Value;
        var to = vm.TimePeriod.To.Value;

        if (ReportService.ReportProviders.TryGetValue(vm.ViewName, out var report))
        {
            if (!report.IsAvailable())
                return this.CreateAPIError(503, "view-unavailable", $"This view is unavailable at this moment");

            var ctx = new Services.Reporting.QueryContext(storeId, from, to);
            await report.Query(ctx, cancellationToken);
            var result = new StoreReportResponse
            {
                Fields = ctx.ViewDefinition?.Fields ?? new List<StoreReportResponse.Field>(),
                Charts = ctx.ViewDefinition?.Charts ?? new List<ChartDefinition>(),
                Data = ctx.Data.Select(d => new JArray(d)).ToList(),
                From = from,
                To = to
            };
            return Json(result);
        }
        
        ModelState.AddModelError(nameof(vm.ViewName), "View doesn't exist");
        return this.CreateValidationError(ModelState);
    }
}

