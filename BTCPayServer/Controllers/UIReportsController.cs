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
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers;

[Authorize(Policy = Policies.CanViewReports, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
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

    [HttpGet("stores/{storeId}/reports")]
    [AcceptMediaTypeConstraint("text/html")]
    [Authorize(Policy = Policies.CanViewReports, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> StoreReports(
        string storeId,
        StoreReportsViewModel? model = null,
        bool fakeData = false,
        CancellationToken cancellation = default)
    {
        model ??= new StoreReportsViewModel();
        var search = model.GetSearch();
        if (model.FilterCommand is not null)
            return model.Redirect(Request);
        if (search.GetExplicitTimeZone() is null)
            return View(model);
        var result = await Api.StoreReports(storeId, search, cancellation);
        if (result is not ObjectResult { Value: StoreReportResponse reportResponse })
            return result;

        if (fakeData && Env.CheatMode)
            reportResponse.Data = Generate(reportResponse.Fields).Select(r => new JArray(r)).ToList();

        model.InvoiceTemplateUrl = Url.Action(nameof(UIInvoiceController.Invoice), "UIInvoice", new { invoiceId = "INVOICE_ID" });
        model.ExplorerTemplateUrls = TransactionLinkProviders.ToDictionary(p => p.Key, p => p.Value.BlockExplorerLink?.Replace("{0}", "TX_ID"));
        model.AvailableViews = ReportService.ReportProviders
            .Values
            .Where(r => r.IsAvailable())
            .Select(k => k.Name)
            .OrderBy(k => k).ToList();
        model.Result = reportResponse;
        return View(model);
    }
}
