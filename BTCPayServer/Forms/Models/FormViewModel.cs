using System.Collections.Generic;
using BTCPayServer.Abstractions.Form;

namespace BTCPayServer.Forms.Models;

public class FormViewModel
{
    public string LogoFileId { get; set; }
    public string CssFileId { get; set; }
    public string BrandColor { get; set; }
    public string StoreName { get; set; }
    public string FormName { get; set; }
    public string RedirectUrl { get; set; }
    public Form Form { get; set; }
    public string AspController { get; set; }
    public string AspAction { get; set; }
    public Dictionary<string, string> RouteParameters { get; set; } = new();
    public Dictionary<string, string> FormParameters { get; set; } = new();
}
