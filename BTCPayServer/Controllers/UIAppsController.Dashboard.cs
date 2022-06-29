using BTCPayServer.Components.AppSales;
using BTCPayServer.Components.AppTopItems;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class UIAppsController
    {
        [HttpGet("{appId}/dashboard/app-top-items")]
        public IActionResult AppTopItems(string appId)
        {
            var app = HttpContext.GetAppData();
            if (app == null)
                return NotFound();
            
            app.StoreData = GetCurrentStore();

            var vm = new AppTopItemsViewModel { App = app };
            return ViewComponent("AppTopItems", new { vm });
        }
        
        [HttpGet("{appId}/dashboard/app-sales")]
        public IActionResult AppSales(string appId)
        {
            var app = HttpContext.GetAppData();
            if (app == null)
                return NotFound();
            
            app.StoreData = GetCurrentStore();

            var vm = new AppSalesViewModel { App = app };
            return ViewComponent("AppSales", new { vm });
        }
    }
}
