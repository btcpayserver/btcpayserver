using System.Collections.Generic;

namespace BTCPayServer.Models.WalletViewModels;

public class WalletLabelsModel
{
    public WalletId WalletId { get; set; }
    public IEnumerable<WalletLabelModel> Labels { get; set; }
}

public class WalletLabelModel
{
    public string Label { get; set; }
    public string Color { get; set; }
    public string TextColor { get; set; }
}
