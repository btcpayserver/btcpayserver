#nullable enable
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.DateFormatterOptions;

public class DeclareDateFormatterOptions(DateFormatterOptionsProvider dateFormatterOptionsProvider) : ViewComponent
{
    public IViewComponentResult Invoke()
    {
        var options = dateFormatterOptionsProvider.GetServerDateFormatterOptions();
        options.DateStyle = "short";
        options.TimeStyle = "short";
        return View(ViewData.GetDateFormatterOptions() ?? options);
    }
}
