using System.Collections.Generic;
using BTCPayServer.Plugins.Emails.Views;

namespace BTCPayServer;

public static class EmailsExtensions
{
    public static List<EmailTriggerViewModel.PlaceHolder> AddStoresPlaceHolders(this List<EmailTriggerViewModel.PlaceHolder> placeholders)
    {
        placeholders.Insert(0, new("{Store.Name}", "The name of the store"));
        placeholders.Insert(0, new("{Store.Id}", "The id of the store"));
        return placeholders;
    }
}
