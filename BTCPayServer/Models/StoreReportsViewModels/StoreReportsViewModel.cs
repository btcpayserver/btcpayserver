using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.GreenField;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.Models.StoreReportsViewModels;

public class StoreReportsViewModel : BasePagingViewModel
{
    [BindNever]
    public string InvoiceTemplateUrl { get; set; } = "";
    [BindNever]
    public Dictionary<PaymentMethodId,string> ExplorerTemplateUrls { get; set; } = [];
    [BindNever]
    public List<string> AvailableViews { get; set; } = [];
    [BindNever]
    public StoreReportResponse Result { get; set; }
    public string ViewName { get; set; }
    public override int CurrentPageCount => Result?.Data?.Count ?? 0;

    protected override void AddUIFilters(SearchString search)
    {
        base.AddUIFilters(search);
        if (!search.ContainsFilter("view"))
            search.SetFilter("view", ViewName ?? GreenfieldReportsController.DefaultReport);
        // By default, search should be thismonth
        if (FilterCommand == "alltime")
        {
            search.SetDateRange("alltime");
        }
        else
        {
            if (!search.HasArrayFilter("startdate") && !search.HasArrayFilter("enddate") && !search.HasArrayFilter("daterange"))
                search.SetDateRange("thismonth");
        }
    }
}
