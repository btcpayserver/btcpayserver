using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Models;

public class StoreBrandingViewModel
{
    public string BrandColor { get; set; }
    public string LogoUrl { get; set; }
    public string CssUrl { get; set; }
    
    public StoreBrandingViewModel()
    {
    }
    public static async Task<StoreBrandingViewModel> CreateAsync(HttpRequest request, UriResolver uriResolver, StoreBlob storeBlob)
    {
        if (storeBlob == null)
            return new StoreBrandingViewModel();
        var result = new StoreBrandingViewModel(storeBlob);
        result.LogoUrl = await uriResolver.Resolve(request.GetAbsoluteRootUri(), storeBlob.LogoUrl);
        result.CssUrl = await uriResolver.Resolve(request.GetAbsoluteRootUri(), storeBlob.CssUrl);
        return result;
    }
    private StoreBrandingViewModel(StoreBlob storeBlob)
    {
        BrandColor = storeBlob.BrandColor;
    }
}
