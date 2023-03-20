using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Components.AppSales;
using BTCPayServer.Components.AppTopItems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class UIAppsController
    {
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("{appId}/dashboard/app-top-items")]
        public IActionResult AppTopItems(string appId)
        {
            var app = HttpContext.GetAppData();
            if (app == null)
                return NotFound();

            app.StoreData = GetCurrentStore();

            return ViewComponent("AppTopItems", new { appId = app.Id, appType = app.AppType });
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("{appId}/dashboard/app-sales")]
        public IActionResult AppSales(string appId)
        {
            var app = HttpContext.GetAppData();
            if (app == null)
                return NotFound();

            app.StoreData = GetCurrentStore();
            return ViewComponent("AppSales", new { appId = app.Id, appType = app.AppType });
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("{appId}/dashboard/app-sales/{period}")]
        public async Task<IActionResult> AppSales(string appId, AppSalesPeriod period)
        {
            var app = HttpContext.GetAppData();
            if (app == null)
                return NotFound();

            app.StoreData = GetCurrentStore();

            var days = period switch
            {
                AppSalesPeriod.Week => 7,
                AppSalesPeriod.Month => 30,
                _ => throw new ArgumentException($"AppSalesPeriod {period} does not exist.")
            };
            var stats = await _appService.GetSalesStats(app, days);

            return stats == null
                ? NotFound()
                : Json(stats);
        }
    }
}
