using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.Icon;

public class Icon : ViewComponent
{
    public IViewComponentResult Invoke(string symbol, string cssClass = null)
    {
        var vm = new IconViewModel
        {
            Symbol = symbol,
            CssClass = cssClass
        };
        return View(vm);
    }
}
