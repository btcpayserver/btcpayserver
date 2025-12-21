using System.Threading.Tasks;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer.Components.AppSales;

public enum AppSalesPeriod
{
    Week,
    Month
}

public class AppSales : ViewComponent
{
    private readonly AppService _appService;

    public AppSales(AppService appService)
    {
        _appService = appService;
    }

    public async Task<IViewComponentResult> InvokeAsync(string appId, string appType)
    {
        var type = _appService.GetAppType(appType);
        if (type is not IHasSaleStatsAppType || type is not AppBaseType appBaseType)
            return new HtmlContentViewComponentResult(new StringHtmlContent(string.Empty));
        var vm = new AppSalesViewModel
        {
            Id = appId,
            AppType = appType,
            DataUrl = Url.Action("AppSales", "UIApps", new { appId }),
            InitialRendering = HttpContext.GetAppData()?.Id != appId
        };
        if (vm.InitialRendering)
            return View(vm);

        var app = HttpContext.GetAppData();
        var stats = await _appService.GetSalesStats(app);
        vm.SalesCount = stats.SalesCount;
        vm.Series = stats.Series;
        vm.AppType = app.AppType;
        vm.AppUrl = await appBaseType.ConfigureLink(app);
        vm.Name = app.Name;

        return View(vm);
    }
}
