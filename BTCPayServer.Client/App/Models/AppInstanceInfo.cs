#nullable enable
namespace BTCPayServer.Client.App.Models;

public class AppInstanceInfo
{
    public string BaseUrl { get; set; } = null!;
    public string ServerName { get; set; } = null!;
    public string? ContactUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? CustomThemeCssUrl { get; set; }
    public string? CustomThemeExtension { get; set; }
    public bool RegistrationEnabled { get; set; }
}
