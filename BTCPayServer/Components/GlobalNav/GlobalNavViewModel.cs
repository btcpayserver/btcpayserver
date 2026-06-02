using BTCPayServer.Components.MainNav;

namespace BTCPayServer.Components.GlobalNav
{
    public class GlobalNavViewModel
    {
        public string UserName { get; set; }
        public string UserImageUrl { get; set; }
        public string ContactUrl { get; set; }
        public bool DockerDeployment { get; set; }
        public string CurrentStoreId { get; set; }
        public MainNavViewModel MainNav { get; set; }
    }
}
