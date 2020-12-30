using NBXplorer.Models;

namespace BTCPayServer.Models.StoreViewModels
{
    public class GenerateWalletViewModel : DerivationSchemeViewModel
    {
        public string StoreId { get; set; }

        public GenerateWalletRequest Request { get; set; } = new GenerateWalletRequest();
    }
}
