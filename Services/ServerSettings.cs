using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Services;

public class ServerSettings
{
    [Display(Name = "Server Name")]
    public string ServerName { get; set; }

    [Display(Name = "Contact URL")]
    public string ContactUrl { get; set; }
    [Display(Name = "Base URL")]
    public string BaseUrl { get; set; }
}
