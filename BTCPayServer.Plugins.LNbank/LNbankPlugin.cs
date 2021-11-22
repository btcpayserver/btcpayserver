using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Client;
using BTCPayServer.Plugins.LNbank.Extensions;
using BTCPayServer.Plugins.LNbank.Hubs;
using BTCPayServer.Plugins.LNbank.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.LNbank
{
    public class LNbankPlugin : BaseBTCPayServerPlugin
    {
        public override string Name { get; } = "LNbank";
        public override string Identifier { get; } = "BTCPayServer.Plugins.LNbank";
        public override string Description { get; } = "This is a description of the loaded test extension!";

        public override void Execute(IServiceCollection services)
        {
            services.AddSingleton<IUIExtension>(new UIExtension("LNbankNavExtension", "header-nav"));
            services.AddSingleton<IUIExtension>(new UIExtension("LNPaymentMethodSetupTabhead", "ln-payment-method-setup-tabhead"));
            services.AddSingleton<IUIExtension>(new UIExtension("LNPaymentMethodSetupTab", "ln-payment-method-setup-tab"));
            services.AddSingleton<LNbankPluginDbContextFactory>();
            services.AddDbContext<LNbankPluginDbContext>((provider, o) =>
            {
                var factory = provider.GetRequiredService<LNbankPluginDbContextFactory>();
                factory.ConfigureBuilder(o);
            });
            services.AddAppServices();
            services.AddAppAuthentication();
        }

        public override void Execute(IApplicationBuilder applicationBuilder, IServiceProvider applicationBuilderApplicationServices)
        {
            base.Execute(applicationBuilder, applicationBuilderApplicationServices);
            applicationBuilderApplicationServices.GetService<LNbankPluginDbContextFactory>().CreateContext().Database.Migrate();

            applicationBuilder.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<TransactionHub>("/Plugins/LNbank/Hubs/Transaction");
            });
        }
    }
}
