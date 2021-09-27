using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.ThemeSwitch
{
    public class ThemeSwitch : ViewComponent
    {
        public IViewComponentResult Invoke(string cssClass = null, string responsive = null)
        {
            var vm = new ThemeSwitchViewModel
            {
                CssClass = cssClass,
                Responsive = responsive
            };
            return View(vm);
        }
    }
}
