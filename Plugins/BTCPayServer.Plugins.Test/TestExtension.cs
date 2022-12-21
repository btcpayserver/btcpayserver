using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Plugins.Test.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Test
{
    public class TestPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier { get; } = "BTCPayServer.Plugins.Test";
        public override string Name { get; } = "Test Plugin!";
        public override string Description { get; } = "This is a description of the loaded test extension!";

        public override void Execute(IServiceCollection services)
        {
            services.AddSingleton<IUIExtension>(new UIExtension("TestExtensionNavExtension", "header-nav"));
            services.AddHostedService<ApplicationPartsLogger>();
            services.AddHostedService<TestPluginMigrationRunner>();
            services.AddSingleton<TestPluginService>();
            services.AddSingleton<TestPluginDbContextFactory>();
            services.AddDbContext<TestPluginDbContext>((provider, o) =>
            {
                var factory = provider.GetRequiredService<TestPluginDbContextFactory>();
                factory.ConfigureBuilder(o);
            });
        }
    }
}
