using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Models.StoreReportsViewModels;

public class StoreReportsViewModel
{
    public string InvoiceTemplateUrl { get; set; }
    public Dictionary<string,string> ExplorerTemplateUrls { get; set; }
    public StoreReportRequest Request { get; set; }
    public List<string> AvailableViews { get; set; }
    public StoreReportResponse Result { get; set; }
}
