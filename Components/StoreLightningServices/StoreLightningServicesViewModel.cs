using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;

namespace BTCPayServer.Components.StoreLightningServices;

public class StoreLightningServicesViewModel
{
    public string StoreId { get; set; }
    public string CryptoCode { get; set; }
    public LightningNodeType? LightningNodeType { get; set; }
    public List<AdditionalServiceViewModel> Services { get; set; }
}
