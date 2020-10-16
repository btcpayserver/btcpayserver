using System;
using BTCPayServer.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Extensions.Test
{
    public class TestExtension: IBTCPayServerExtension
    {
        public string Identifier { get; } = "BTCPayServer.Extensions.Test";
        public string Name { get; } = "Test Plugin!";
        public Version Version { get; } = new Version(1,0,0,0);
        public string Description { get; } = "This is a description of the loaded test extension!";
        public bool SystemExtension { get; set; }

        public void Execute(IApplicationBuilder applicationBuilder, IServiceProvider applicationBuilderApplicationServices)
        {
            
        }

        public void Execute(IServiceCollection services)
        {
            services.AddSingleton<IUIExtension>(new UIExtension("TestExtensionNavExtension", "header-nav"));
            services.AddHostedService<ApplicationPartsLogger>();
        }
    }
}
