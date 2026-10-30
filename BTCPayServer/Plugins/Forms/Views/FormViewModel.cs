using System.Collections.Generic;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.Forms.Models;

public class FormViewModel
{
    public string StoreName { get; set; }
    public string FormName { get; set; }
    public Form Form { get; set; }
    [BindNever]
    public string AspController { get; set; }
    [BindNever]
    public string AspAction { get; set; }
    [BindNever]
    public Dictionary<string, string> RouteParameters { get; set; } = new();
    public MultiValueDictionary<string, string> FormParameters { get; set; } = new();
    public StoreBrandingViewModel StoreBranding { get; set; }
    public string FormParameterPrefix { get; set; }
}
