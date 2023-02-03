using BTCPayServer.Services;

namespace BTCPayServer.Components.LabelManager
{
    public class LabelViewModel
    {
        public string[] SelectedLabels { get; set; }
        public WalletObjectId ObjectId { get; set; }
    }
}
