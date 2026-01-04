using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Models;

public class StoreBrandingViewModel
{
    public string BrandColor { get; set; }
    public bool ApplyBrandColorToBackend { get; set; }
    public string LogoUrl { get; set; }
    public string CssUrl { get; set; }
    
    public StoreBrandingViewModel()
    {
    }
    public static async Task<StoreBrandingViewModel> CreateAsync(HttpRequest request, UriResolver uriResolver, StoreBlob storeBlob)
    {
        if (storeBlob == null)
            return new StoreBrandingViewModel();
        var result = new StoreBrandingViewModel(storeBlob)
        {
            LogoUrl = await uriResolver.Resolve(request.GetAbsoluteRootUri(), storeBlob.LogoUrl),
            CssUrl = await uriResolver.Resolve(request.GetAbsoluteRootUri(), storeBlob.CssUrl)
        };
        return result;
    }
    private StoreBrandingViewModel(StoreBlob storeBlob)
    {
        BrandColor = storeBlob.BrandColor;
        ApplyBrandColorToBackend = storeBlob.ApplyBrandColorToBackend;
    }
}
