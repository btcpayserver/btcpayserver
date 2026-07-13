using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.GreenField;
using BTCPayServer.Payments;

namespace BTCPayServer.Models.StoreReportsViewModels;

public class StoreReportsViewModel : BasePagingViewModel
{
    public string InvoiceTemplateUrl { get; set; } = "";
    public Dictionary<PaymentMethodId,string> ExplorerTemplateUrls { get; set; } = [];
    public List<string> AvailableViews { get; set; } = [];
    public StoreReportResponse Result { get; set; }
    public string ViewName { get; set; }
    public override int CurrentPageCount => Result?.Data?.Count ?? 0;

    protected override void AddUIFilters(SearchString search)
    {
        base.AddUIFilters(search);
        if (!search.ContainsFilter("view"))
            search.SetFilter("view", ViewName ?? GreenfieldReportsController.DefaultReport);
        // By default, search should be thismonth
        if (FilterCommand != "alltime")
        {
            if (!search.HasArrayFilter("startdate") && !search.HasArrayFilter("enddate") && !search.HasArrayFilter("daterange"))
                search.SetDateRange("thismonth");
        }
    }
}
