namespace BTCPayApp.CommonServer;

public class AppInstanceInfo
{
    public string BaseUrl { get; set; }
    public string ServerName { get; set; }
    public string? ContactUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? CustomThemeCssUrl { get; set; }
    public string? CustomThemeExtension { get; set; }
    public bool RegistrationEnabled { get; set; }
}
