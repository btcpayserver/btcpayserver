using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Services;

public enum ThemeExtension
{
    [Display(Name = "Does not extend a BTCPay Server theme, fully custom")]
    Custom,
    [Display(Name = "Extends the BTCPay Server Light theme")]
    Light,
    [Display(Name = "Extends the BTCPay Server Dark theme")]
    Dark
}

public class ThemeSettings
{
    [Display(Name = "Use custom theme")]
    public bool CustomTheme { get; set; }

    [Display(Name = "Custom Theme Extension Type")]
    public ThemeExtension CustomThemeExtension { get; set; }

    public string CustomThemeCssUrl { get; set; }

    public string LogoUrl { get; set; }

    [Obsolete("Use CustomThemeCssUrl instead")]
    public string CustomThemeFileId { get; set; }
    [Obsolete("Use LogoUrl instead")]
    public string LogoFileId { get; set; }

    public bool FirstRun { get; set; } = true;

    public override string ToString()
    {
        // no logs
        return string.Empty;
    }
}
