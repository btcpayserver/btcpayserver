using System.ComponentModel.DataAnnotations;
using BTCPayServer.Abstractions;
using BTCPayServer.Abstractions.Extensions;

namespace BTCPayServer.Services;

public class ServerSettings
{
    [Display(Name = "Server Name")]
    public string ServerName { get; set; }

    [Display(Name = "Contact URL")]
    public string ContactUrl { get; set; }
    [Display(Name = "Base URL")]
    public string BaseUrl { get; set; }

    public bool UpdateBaseUrlIfUnset(RequestBaseUrl baseUrl)
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return false;

        BaseUrl = baseUrl.ToString().WithoutEndingSlash();
        return true;
    }
}
