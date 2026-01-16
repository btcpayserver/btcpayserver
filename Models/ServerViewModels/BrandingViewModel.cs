using System.ComponentModel.DataAnnotations;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace BTCPayServer.Models.ServerViewModels;

public class BrandingViewModel
{
    // Server
    [Display(Name = "Server Name")]
    public string ServerName { get; set; }

    [Display(Name = "Contact URL")]
    public string ContactUrl { get; set; }

    // Theme
    [Display(Name = "Use custom theme")]
    public bool CustomTheme { get; set; }

    [Display(Name = "Custom Theme Extension Type")]
    public ThemeExtension CustomThemeExtension { get; set; }

    [Display(Name = "Custom Theme File")]
    [JsonIgnore]
    public IFormFile CustomThemeFile { get; set; }

    public string CustomThemeCssUrl { get; set; }

    [Display(Name = "Logo")]
    [JsonIgnore]
    public IFormFile LogoFile { get; set; }

    public string LogoUrl { get; set; }
    [Display(Name = "Base URL")]
    public string BaseUrl { get; set; }
}
