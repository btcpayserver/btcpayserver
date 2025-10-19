using System.Collections.Generic;

namespace BTCPayServer.Plugins.Emails.Views;

/// <summary>
/// This view model is used in StoreEmailRulesManage.cshtml, to display the different triggers that can be used to send emails
/// </summary>
public class EmailTriggerViewModel
{
    public string Type { get; set; }
    public string Description { get; set; }
    public string SubjectExample { get; set; }
    public string BodyExample { get; set; }
    public bool CanIncludeCustomerEmail { get; set; }

    public class PlaceHolder(string name, string description)
    {
        public string Name { get; set; } = name;
        public string Description { get; set; } = description;
    }

    public List<PlaceHolder> PlaceHolders { get; set; } = new();
}
