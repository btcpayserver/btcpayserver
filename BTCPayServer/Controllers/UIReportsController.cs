#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.GreenField;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models.StoreReportsViewModels;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Services.Reporting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers;

[Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public partial class UIReportsController : Controller
{
    private readonly IScopeProvider _scopeProvider;

    public UIReportsController(
        BTCPayNetworkProvider networkProvider,
        ApplicationDbContextFactory dbContextFactory,
        GreenfieldReportsController api,
        ReportService reportService,
        DisplayFormatter displayFormatter,
        BTCPayServerEnvironment env, IScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
        Api = api;
        ReportService = reportService;
        Env = env;
        DBContextFactory = dbContextFactory;
        NetworkProvider = networkProvider;
        DisplayFormatter = displayFormatter;
    }
    private BTCPayNetworkProvider NetworkProvider { get; }
    private DisplayFormatter DisplayFormatter { get; }
    public GreenfieldReportsController Api { get; }
    public ReportService ReportService { get; }
    public BTCPayServerEnvironment Env { get; }
    public ApplicationDbContextFactory DBContextFactory { get; }

    [HttpPost("stores/{storeId}/reports")]
    [AcceptMediaTypeConstraint("application/json")]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> StoreReportsJson(string storeId, [FromBody] StoreReportRequest? request = null, bool fakeData = false, CancellationToken cancellation = default)
    {
        var result = await Api.StoreReports(storeId, request, cancellation);
        if (fakeData && Env.CheatMode)
        {
            var r = (StoreReportResponse)((JsonResult)result!).Value!;
            r.Data = Generate(r.Fields).Select(r => new JArray(r)).ToList();
        }
        return result;
    }

    [HttpGet("stores/{storeId}/reports")]
    [AcceptMediaTypeConstraint("text/html")]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult StoreReports(
        string storeId,
        string ? viewName = null)
    {
        var vm = new StoreReportsViewModel
        {
            InvoiceTemplateUrl = Url.Action(nameof(UIInvoiceController.Invoice), "UIInvoice", new { invoiceId = "INVOICE_ID" }),
            ExplorerTemplateUrls = NetworkProvider.GetAll().ToDictionary(network => network.CryptoCode, network => network.BlockExplorerLink?.Replace("{0}", "TX_ID")),
            Request = new StoreReportRequest { ViewName = viewName ?? "Payments" },
            AvailableViews = ReportService.ReportProviders
                .Values
                .Where(r => r.IsAvailable())
                .Select(k => k.Name)
                .OrderBy(k => k).ToList()
        };
        return View(vm);
    }
    [HttpGet("reports/dynamic")]
    [AcceptMediaTypeConstraint("text/html")]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult DynamicReport(
        string? reportName)
    {

        if(Request.Query.TryGetValue("viewName", out var vn) && vn.Count == 1)
        {
            if (ReportService.ReportProviders.TryGetValue(vn[0], out var report) && report is PostgresReportProvider)
            {
                return RedirectToAction(nameof(DynamicReport), new {reportName = vn[0]});
            }
        }
        if (reportName is not null)
        {
            if (!ReportService.ReportProviders.TryGetValue(reportName, out var report))
            {
                return NotFound();
            }

            if (report is not PostgresReportProvider postgresReportProvider)
            {
                return NotFound();
            }

            return View(new DynamicReportViewModel()
            {
                Name = reportName,
                Sql = postgresReportProvider.Setting.Sql,
                AllowForNonAdmins = postgresReportProvider.Setting.AllowForNonAdmins
            });
        }

        return View(new DynamicReportViewModel());

    }  
    [HttpPost("reports/dynamic")]
    [AcceptMediaTypeConstraint("text/html")]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DynamicReport(
        string reportName, DynamicReportViewModel vm, string command)
    {
        ModelState.Clear();
        if (command == "remove" && reportName is not null)
        {
            await ReportService.UpdateDynamicReport(reportName, null);
            TempData[WellKnownTempData.SuccessMessage] = $"Report {reportName} removed";
            return RedirectToAction(nameof(DynamicReport));
        }
        
        string msg = null;
        if(string.IsNullOrEmpty(vm.Sql))
        {
            ModelState.AddModelError(nameof(vm.Sql), "SQL is required");
        }
        else
        {
            try
            {
                var context = new QueryContext(_scopeProvider.GetCurrentStoreId(), DateTimeOffset.MinValue,
                    DateTimeOffset.MaxValue);
                await PostgresReportProvider.ExecuteQuery(DBContextFactory, context, vm.Sql, CancellationToken.None);   
                msg = $"Fetched {context.Data.Count} rows with {context.ViewDefinition?.Fields.Count} columns";
                TempData["Data"] = JsonConvert.SerializeObject(context);
            }
            catch (Exception e)
            {
                ModelState.AddModelError(nameof(vm.Sql), "Could not execute SQL: " + e.Message);
            }
        }
        if(string.IsNullOrEmpty(vm.Name))
        {
            ModelState.AddModelError(nameof(vm.Name), "Name is required");
        }
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        await ReportService.UpdateDynamicReport(reportName??vm.Name, vm);
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Html = $"Report {reportName} {(reportName is null ? "created" : "updated")}{(msg is null? string.Empty: $"<br/>{msg})")}"
        });
        TempData[WellKnownTempData.SuccessMessage] = $"Report {reportName} {(reportName is null ? "created" : "updated")}";
       
        return RedirectToAction(nameof(DynamicReport) , new {reportName = reportName??vm.Name});
        
    }
}
