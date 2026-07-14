#nullable enable
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.DateFormatterOptions;

public class DeclareDateFormatterOptions : ViewComponent
{
    public IViewComponentResult Invoke()
        => View();
}
