using BTCPayServer;
using BTCPayServer.Contracts;
using BTCPayServer.Zammad;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Zammad
{
    public static class ZammadExtensions
    {
        public static void AddZammadServices(this IServiceCollection services)
        {
            services.AddHostedService<ZammadHostedService>();
            services.AddSingleton<IMainNavExtension>(provider =>
                new IMainNavExtension.GenericMainNavExtension("LayoutPartials/ZammadNavExtension"));
        }
    }
}
