using System;
using BTCPayServer.Contracts;
using BTCPayServer.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Extensions.Test
{
    public class TestExtension: BaseBTCPayServerExtension
    {
        public  override string Identifier { get; } = "BTCPayServer.Extensions.Test";
        public  override string Name { get; } = "Test Plugin!";
        public  override string Description { get; } = "This is a description of the loaded test extension!";

        public override void Execute(IServiceCollection services)
        {
            services.AddSingleton<IUIExtension>(new UIExtension("TestExtensionNavExtension", "header-nav"));
            services.AddHostedService<ApplicationPartsLogger>();
        }
    }
}
