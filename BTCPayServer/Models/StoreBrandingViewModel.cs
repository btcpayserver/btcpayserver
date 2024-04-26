using BTCPayServer.Data;

namespace BTCPayServer.Models;

public class StoreBrandingViewModel
{
    public string BrandColor { get; set; }
    public string LogoUrl { get; set; }
    public string CssUrl { get; set; }
    
    public StoreBrandingViewModel()
    {
    }
    
    public StoreBrandingViewModel(StoreBlob storeBlob)
    {
        if (storeBlob == null) return;
        BrandColor = storeBlob.BrandColor;
        LogoUrl = storeBlob.LogoUrl;
        CssUrl = storeBlob.CssUrl;
    }
}
