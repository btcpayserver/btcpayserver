#nullable enable
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.DateFormatterOptions;

public class DeclareDateFormatterOptions : ViewComponent
{
    public IViewComponentResult Invoke() => View(ViewData.GetDateFormatterOptions() ?? new()
    {
        DateStyle = "short",
        TimeStyle = "short",
        Locale = "default"
    });
}
