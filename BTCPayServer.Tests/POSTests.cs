using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Plugins.PointOfSale.Controllers;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Services.Apps;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using Xunit.Abstractions;
using static BTCPayServer.Tests.UnitTest1;

namespace BTCPayServer.Tests
{
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class POSTests : UnitTestBase
    {
        public POSTests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUsePoSApp1()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");
            var apps = user.GetController<UIAppsController>();
            var pos = user.GetController<UIPointOfSaleController>();
            var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp(user.StoreId)).Model);
            var appType = AppType.PointOfSale.ToString();
            vm.AppName = "test";
            vm.SelectedAppType = appType;
            Assert.IsType<RedirectToActionResult>(apps.CreateApp(user.StoreId, vm).Result);
            var appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            var app = appList.Apps[0];
            var appData = new AppData { Id = app.Id, StoreDataId = app.StoreId, Name = app.AppName, AppType = appType };
            apps.HttpContext.SetAppData(appData);
            pos.HttpContext.SetAppData(appData);
            var vmpos = await pos.UpdatePointOfSale(app.Id).AssertViewModelAsync<UpdatePointOfSaleViewModel>();
            vmpos.Template = @"
apple:
  price: 5.0
  title: good apple
  disabled: true
orange:
  price: 10.0
donation:
  price: 1.02
  custom: true
";
            Assert.IsType<RedirectToActionResult>(pos.UpdatePointOfSale(app.Id, vmpos).Result);
            await pos.UpdatePointOfSale(app.Id).AssertViewModelAsync<UpdatePointOfSaleViewModel>();
            var publicApps = user.GetController<UIPointOfSaleController>();
            var vmview = await publicApps.ViewPointOfSale(app.Id, PosViewType.Cart).AssertViewModelAsync<ViewPointOfSaleViewModel>();

            // apple shouldn't be available since we it's set to "disabled: true" above
            Assert.Equal(2, vmview.Items.Length);
            Assert.Equal("orange", vmview.Items[0].Title);
            Assert.Equal("donation", vmview.Items[1].Title);
            // orange is available
            Assert.IsType<RedirectToActionResult>(publicApps
                .ViewPointOfSale(app.Id, PosViewType.Cart, 0, null, null, null, null, "orange").Result);
            // apple is not found
            Assert.IsType<NotFoundResult>(publicApps
                .ViewPointOfSale(app.Id, PosViewType.Cart, 0, null, null, null, null, "apple").Result);
        }
    }
}
