using System.Collections.Generic;

namespace BTCPayServer.Models.StoreViewModels;

public class LightningViewModel : LightningNodeViewModel
{
    public List<AdditionalServiceViewModel> Services { get; set; }
}
