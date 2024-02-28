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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using System.Threading;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Forms;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers;

[Authorize(Policy = Policies.CanViewReports, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public partial class UIReportsController : Controller
{
    private readonly FormDataService _formDataService;

    public UIReportsController(
        ApplicationDbContextFactory dbContextFactory,
        GreenfieldReportsController api,
        ReportService reportService,
        DisplayFormatter displayFormatter,
        BTCPayServerEnvironment env,
        BTCPayNetworkProvider networkProvider,
        TransactionLinkProviders transactionLinkProviders,
        FormDataService formDataService)
    {
        _formDataService = formDataService;
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

    [HttpPost("stores/{storeId}/reports/{viewName}")]
    [AcceptMediaTypeConstraint("application/json")]
    [Authorize(Policy = Policies.CanViewReports, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> StoreReportsJson(string storeId, string viewName, bool fakeData = false, CancellationToken cancellation = default)
    {
        
        
        
        var form = ReportService.ReportProviders[viewName].GetForm();
        
        if (Request.HasFormContentType)
        {
            form.ApplyValuesFromForm(Request.Form);
            
        }
        // if (!_formDataService.Validate(form, ModelState))
        // {
        //    if(Request.Headers["Accept"].ToString()?.StartsWith("application/json", StringComparison.InvariantCultureIgnoreCase) is true)
        //    {
        //            return this.CreateValidationError(ModelState);
        //    }
        //    
        //    return StoreReports(storeId, viewName);
        //    
        //     
        // }

        var query = _formDataService.GetValues(form);
        
        var result = await Api.StoreReports(storeId, new StoreReportRequest()
        {
            ViewName = viewName,
            Query = query
        } , cancellation);
        if (fakeData && Env.CheatMode)
        {
            var r = (StoreReportResponse)((JsonResult)result!).Value!;
            r.Data = Generate(r.Fields).Select(r => new JArray(r)).ToList();
        }
        return result;
    }

    [HttpGet("stores/{storeId}/reports/{viewName?}")]
    [AcceptMediaTypeConstraint("text/html")]
    [Authorize(Policy = Policies.CanViewReports, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult StoreReports(
        string storeId,
        string viewName)
    {
        if(viewName is null)
            return RedirectToAction(nameof(StoreReports), new { storeId, viewName = "Payments" });
        var vm = new StoreReportsViewModel
        {
            InvoiceTemplateUrl = Url.Action(nameof(UIInvoiceController.Invoice), "UIInvoice", new { invoiceId = "INVOICE_ID" }),
            ExplorerTemplateUrls = TransactionLinkProviders.ToDictionary(p => p.Key.CryptoCode, p => p.Value.BlockExplorerLink?.Replace("{0}", "TX_ID")),
            // Request = new StoreReportRequest { ViewName = viewName ?? "Payments" },
            AvailableViews = ReportService.ReportProviders
                .Values
                .Where(r => r.IsAvailable())
                .Select(k => k.Name)
                .OrderBy(k => k).ToList()
        };
        return View(vm);
    }
}
