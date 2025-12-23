using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Services;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Emails;

public class ServerTransformer(ISettingsAccessor<ServerSettings> serverSettings) : IEmailTriggerViewModelTransformer, IEmailTriggerEventTransformer
{
    public void Transform(EmailTriggerViewModel viewModel)
    {
        viewModel.PlaceHolders.Add(new("{Server.Name}", ServerNameDoc));
        viewModel.PlaceHolders.Add(new("{Server.ContactUrl}", ContactUrlDoc));
        viewModel.PlaceHolders.Add(new("{Server.BaseUrl}", BaseUrlDoc));
    }

    public const string ServerNameDoc = "The name of the server (Server Settings ➡ Branding)";
    public const string ContactUrlDoc = "The contact URL of the server (Server Settings ➡ Branding)";
    public const string BaseUrlDoc = "The base URL of this server (Server Settings ➡ Branding)";
    public static string[] TranslatedStrings => new[] { ServerNameDoc, ContactUrlDoc, BaseUrlDoc };
    public void Transform(IEmailTriggerEventTransformer.Context context)
    {
        var serverObj = (JObject)(context.TriggerEvent.Model["Server"] ??= new JObject());
        serverObj["Name"] = serverSettings.Settings.ServerName ?? "BTCPay Server";
        serverObj["ContactUrl"] =  serverSettings.Settings.ContactUrl;
        serverObj["BaseUrl"] = serverSettings.Settings.BaseUrl;
    }
}

public class StoreTransformer : IEmailTriggerViewModelTransformer, IEmailTriggerEventTransformer
{
    public void Transform(EmailTriggerViewModel viewModel)
    {
        if (!viewModel.ServerTrigger)
        {
            viewModel.PlaceHolders.Add(new("{Store.Id}", StoreIdDoc));
            viewModel.PlaceHolders.Add(new("{Store.Name}", StoreNameDoc));
            viewModel.PlaceHolders.Add(new("{Store.WebsiteUrl}", StoreUrlDoc));
            viewModel.PlaceHolders.Add(new("{Store.SupportUrl}", SupportUrlDoc));
        }
    }

    public const string StoreNameDoc = "The name of the store";
    public const string StoreIdDoc = "The id of the store";
    public const string StoreUrlDoc = "The website of the store";
    public const string SupportUrlDoc = "The support url of the store";
    public static string[] TranslatedStrings => new[] { StoreIdDoc, StoreNameDoc, StoreUrlDoc, SupportUrlDoc };
    public void Transform(IEmailTriggerEventTransformer.Context context)
    {
        if (context.Store is { } store)
        {
            var storeObj = (JObject)(context.TriggerEvent.Model["Store"] ??= new JObject());
            storeObj["Id"] = store.Id;
            storeObj["Name"] = store.StoreName;
            storeObj["SupportUrl"] = store.GetStoreBlob().StoreSupportUrl;
            storeObj["WebsiteUrl"] = store.StoreWebsite;
        }
    }
}
