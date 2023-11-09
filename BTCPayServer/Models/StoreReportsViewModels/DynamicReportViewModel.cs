using BTCPayServer.Services;

namespace BTCPayServer.Models.StoreReportsViewModels;

public class DynamicReportViewModel:DynamicReportsSettings.DynamicReportSetting
{
    public string Name { get; set; }
    
}
