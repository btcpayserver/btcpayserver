using System.Collections.Generic;

namespace BTCPayServer.Plugins.Emails.Views.Shared;

public class EmailRulesListViewModel
{
    public string StoreId { get; set; }
    public string ModifyPermission { get; set; }
    public bool ShowCustomerEmailColumn { get; set; }
    public List<StoreEmailRuleViewModel> Rules { get; set; }
}
