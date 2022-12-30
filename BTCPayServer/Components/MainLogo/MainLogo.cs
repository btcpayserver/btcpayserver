using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.MainLogo
{
    public class MainLogo : ViewComponent
    {
        public IViewComponentResult Invoke(string cssClass = null)
        {
            var vm = new MainLogoViewModel
            {
                CssClass = cssClass,
            };
            return View(vm);
        }
    }
}
