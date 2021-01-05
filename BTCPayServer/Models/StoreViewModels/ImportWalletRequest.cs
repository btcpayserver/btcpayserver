using NBXplorer.Models;

namespace BTCPayServer.Models.StoreViewModels
{
    public class ImportWalletRequest : GenerateWalletRequest
    {
        public bool AcceptExistingMnemonic { get; set; } = true;
    }
}
