#nullable enable
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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers;

[Authorize(Policy = Policies.CanViewReports, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public partial class UIReportsController : Controller
{
    public UIReportsController(
        ApplicationDbContextFactory dbContextFactory,
        GreenfieldReportsController api,
        ReportService reportService,
        DisplayFormatter displayFormatter,
        BTCPayServerEnvironment env,
        BTCPayNetworkProvider networkProvider,
        TransactionLinkProviders transactionLinkProviders)
    {
        Api = api;
        ReportService = reportService;
        Env = env;
        DBContextFactory = dbContextFactory;
        NetworkProvider = networkProvider;
        DisplayFormatter = displayFormatter;
        TransactionLinkProviders = transactionLinkProviders;
    }
    private BTCPayNetworkProvider NetworkProvider { get; }
    private DisplayFormatter DisplayFormatter { get; }
    public GreenfieldReportsController Api { get; }
    public ReportService ReportService { get; }
    public BTCPayServerEnvironment Env { get; }
    public ApplicationDbContextFactory DBContextFactory { get; }
    public TransactionLinkProviders TransactionLinkProviders { get; }

    [HttpPost("stores/{storeId}/reports")]
    [AcceptMediaTypeConstraint("application/json")]
    [Authorize(Policy = Policies.CanViewReports, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
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
    [Authorize(Policy = Policies.CanViewReports, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult StoreReports(
        string storeId,
        string ? viewName = null)
    {
        var vm = new StoreReportsViewModel
        {
            InvoiceTemplateUrl = Url.Action(nameof(UIInvoiceController.Invoice), "UIInvoice", new { invoiceId = "INVOICE_ID" }),
            ExplorerTemplateUrls = TransactionLinkProviders.ToDictionary(p => p.Key.CryptoCode, p => p.Value.BlockExplorerLink?.Replace("{0}", "TX_ID")),
            Request = new StoreReportRequest { ViewName = viewName ?? "Payments" },
            AvailableViews = ReportService.ReportProviders
                .Values
                .Where(r => r.IsAvailable())
                .Select(k => k.Name)
                .OrderBy(k => k).ToList()
        };
        return View(vm);
    }
}
