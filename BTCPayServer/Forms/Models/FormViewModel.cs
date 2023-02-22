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
    public Form Form { get; set; }
    public string AspController { get; set; }
    public string AspAction { get; set; }
    public Dictionary<string, string> RouteParameters { get; set; } = new();
    public MultiValueDictionary<string, string> FormParameters { get; set; } = new();
    public string FormParameterPrefix { get; set; }
}
