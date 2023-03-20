using BTCPayServer.Services;

namespace BTCPayServer.Components.LabelManager
{
    public class LabelViewModel
    {
        public string[] SelectedLabels { get; set; }
        public WalletObjectId WalletObjectId { get; set; }
        public bool ExcludeTypes { get; set; }
        public bool DisplayInline { get; set; }
    }
}
