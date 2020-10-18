using BTCPayServer.Contracts;
using BTCPayServer.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Extensions.BlockExplorerLinks
{
    public class BlockExplorerLinkExtension: BaseBTCPayServerExtension
    {
        public override string Identifier { get; } = "BTCPayServer.Extensions.BlockExplorerLinks";
        public override string Name { get; } = "Block Explorer Changer";
        public  override string Description { get; } = "This extension allows you to change the default block explorers";

        public override void Execute(IServiceCollection services)
        {
            services.AddSingleton<IUIExtension>(new UIExtension("BlockExplorerServerNavLink", "server-nav"));
            services.AddStartupTask<BlockExplorerLinkStartupTask>();
            base.Execute(services);
        }
    }
}
