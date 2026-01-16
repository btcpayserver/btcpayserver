using BTCPayServer.Abstractions;
using BTCPayServer.Plugins.Emails;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Plugins.Monetization.Controllers;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Monetization;

public class MonetizationEmailTriggerTransformer(
    ISettingsAccessor<MonetizationSettings> monetization,
    LinkGenerator linkGenerator,
    ISettingsAccessor<ServerSettings> serverSettings)  : IEmailTriggerViewModelTransformer, IEmailTriggerEventTransformer
{
    public void Transform(EmailTriggerViewModel viewModel)
    {
        if (serverSettings.Settings.BaseUrl != null && monetization.Settings.IsSetup() && !viewModel.ServerTrigger)
        {
            viewModel.PlaceHolders.Add(new("{Links.BillingPortalUrl}", BillingPortalUrlDoc));
        }
    }
    public const string BillingPortalUrlDoc = "The billing portal URL of the logged account";
    public static string[] TranslatedStrings => new[] { BillingPortalUrlDoc };

    public void Transform(IEmailTriggerEventTransformer.Context context)
    {
        if (serverSettings.Settings.BaseUrl is string baseUrl
            && context.Store is not null
            && RequestBaseUrl.TryFromUrl(baseUrl, out var r)
            && monetization.Settings.IsSetup())
        {
            var userObj = (JObject)(context.TriggerEvent.Model["Links"] ??= new JObject());
            userObj["BillingPortalUrl"] = linkGenerator.UserManageBillingLink(r);
        }
    }
}
