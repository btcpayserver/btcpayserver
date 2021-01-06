using NBXplorer.Models;

namespace BTCPayServer.Models.StoreViewModels
{
    public class WalletSetupRequest : GenerateWalletRequest
    {
        public bool RequireExistingMnemonic { get; set; }
    }
}
