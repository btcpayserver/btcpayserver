using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Services;

public class ServerSettings
{
    [Display(Name = "Server Name")]
    public string ServerName { get; set; } = "BTCPay Server";

    [Display(Name = "Contact URL")]
    public string ContactUrl { get; set; }
}
