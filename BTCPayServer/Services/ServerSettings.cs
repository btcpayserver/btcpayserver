using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Services;

public class ServerSettings
{
    [Display(Name = "Server Name")]
    public string ServerName { get; set; }

    [Display(Name = "Contact URL")]
    public string ContactUrl { get; set; }
}
