using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Events;
using BTCPayServer.Services.Apps;

namespace BTCPayServer.Plugins.Subscriptions;

public static class AppServiceSubscriptionsExtensions
{
    public static async Task<(string AppId, string OfferingId)> CreateOffering(this AppService appService, string storeId, string name)
    {
        var app = new AppData()
        {
            Name = name,
            AppType = SubscriptionsAppType.AppType,
            StoreDataId = storeId
        };
        app.SetSettings(new SubscriptionsAppType.AppConfig());
        await appService.UpdateOrCreateApp(app, sendEvents: false);

        await using var ctx = appService.ContextFactory.CreateContext();
        var o = new OfferingData()
        {
            AppId = app.Id,
        };
        ctx.Offerings.Add(o);
        await ctx.SaveChangesAsync();
        app.SetSettings(new SubscriptionsAppType.AppConfig()
        {
            OfferingId = o.Id
        });
        await appService.UpdateOrCreateApp(app, sendEvents: false);
        appService.EventAggregator.Publish(new AppEvent.Created(app));
        return (app.Id, o.Id);
    }
}
