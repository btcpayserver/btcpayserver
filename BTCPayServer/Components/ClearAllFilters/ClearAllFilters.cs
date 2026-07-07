#nullable enable
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.ClearAllFilters;

[ViewComponent]
public class ClearAllFilters : ViewComponent
{
    public IViewComponentResult Invoke(SearchString? search)
    => View(search);
}
