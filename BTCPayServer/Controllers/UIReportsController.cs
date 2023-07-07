#nullable enable
using System;
using Dapper;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.GreenField;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models.StoreReportsViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using System.Text.Json.Nodes;
using Org.BouncyCastle.Ocsp;
using System.Threading;

namespace BTCPayServer.Controllers;

[Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public class UIReportsController : Controller
{
    public UIReportsController(
        BTCPayNetworkProvider networkProvider,
        ApplicationDbContextFactory dbContextFactory,
        GreenfieldReportsController api,
        ReportService reportService)
    {
        Api = api;
        ReportService = reportService;
        DBContextFactory = dbContextFactory;
        NetworkProvider = networkProvider;
    }
    private BTCPayNetworkProvider NetworkProvider { get; }
    public GreenfieldReportsController Api { get; }
    public ReportService ReportService { get; }
    public ApplicationDbContextFactory DBContextFactory { get; }

    [HttpPost("stores/{storeId}/reports")]
    [AcceptMediaTypeConstraint("application/json")]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> StoreReportsJson(string storeId, [FromBody] StoreReportRequest? request = null, CancellationToken cancellation = default)
    {
        return await Api.StoreReports(storeId, request, cancellation);
    }

    [HttpGet("stores/{storeId}/reports")]
    [AcceptMediaTypeConstraint("text/html")]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult StoreReports(
        string storeId,
        string ? viewName = null)
    {
        var vm = new StoreReportsViewModel()
        {
            InvoiceTemplateUrl = this.Url.Action(nameof(UIInvoiceController.Invoice), "UIInvoice", new { invoiceId = "INVOICE_ID" }),
            ExplorerTemplateUrls = NetworkProvider.GetAll().ToDictionary(network => network.CryptoCode, network => network.BlockExplorerLink?.Replace("{0}", "TX_ID")),
            Request = new StoreReportRequest()
            {
                ViewName = viewName ?? "Payments"
            }
        };
        vm.AvailableViews = ReportService.ReportProviders
                                         .Values
                                         .Where(r => r.IsAvailable())
                                         .SelectMany(k => k.CreateViewDefinitions())
                                         .Select(k => k.Name)
                                         .OrderBy(k => k).ToList();
        return View(vm);
    }
}
