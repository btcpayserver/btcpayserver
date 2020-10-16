using System;
using BTCPayServer.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Extensions.BlockExplorerLinks
{
    public class BlockExplorerLinkExtension: IBTCPayServerExtension
    {
        public string Identifier { get; } = "BTCPayServer.Extensions.BlockExplorerLinks";
        public string Name { get; } = "Block Explorer Changer";
        public Version Version { get; } = new Version(1,0,0,0);
        public string Description { get; } = "This extension allows you to change the default block explorers";
        public bool SystemExtension { get; set; }

        public void Execute(IApplicationBuilder applicationBuilder, IServiceProvider applicationBuilderApplicationServices)
        {
            
        }

        public void Execute(IServiceCollection services)
        {
            services.AddSingleton<IUIExtension>(new UIExtension("BlockExplorerServerNavLink", "server-nav"));
            services.AddStartupTask<BlockExplorerLinkStartupTask>();
        }
    }
}
