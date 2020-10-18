using System;
using BTCPayServer.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Test
{
    public class TestExtension: IBTCPayServerExtension
    {
        public string Identifier { get; } = "BTCPayServer.Test";
        public string Name { get; } = "Test Plugin!";
        public Version Version { get; } = new Version(1,0,0,0);
        public string Description { get; } = "This is a description of the loaded test extension!";
        public void Execute(IApplicationBuilder applicationBuilder, IServiceProvider applicationBuilderApplicationServices)
        {
            
        }

        public void Execute(IServiceCollection services)
        {
            services.AddSingleton<INavExtension>(new NavExtension("TestExtensionNavExtension", "header-nav"));
            services.AddHostedService<ApplicationPartsLogger>();
        }
    }
}
