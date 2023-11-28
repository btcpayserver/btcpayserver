using BTCPayServer.Data;

namespace BTCPayServer.Models;

public class StoreBrandingViewModel
{
    public string BrandColor { get; set; }
    public string LogoFileId { get; set; }
    public string CssFileId { get; set; }
    public string CustomCSSLink { get; set; }
    public string EmbeddedCSS { get; set; }
    
    public StoreBrandingViewModel()
    {
    }
    
    public StoreBrandingViewModel(StoreBlob storeBlob)
    {
        if (storeBlob == null) return;
        BrandColor = storeBlob.BrandColor;
        LogoFileId = storeBlob.LogoFileId;
        CssFileId = storeBlob.CssFileId;
    }
}
