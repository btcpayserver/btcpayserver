using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Models.AppViewModels;
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
            var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp(user.StoreId)).Model);
            vm.AppName = "test";
            vm.SelectedAppType = AppType.PointOfSale.ToString();
            Assert.IsType<RedirectToActionResult>(apps.CreateApp(user.StoreId, vm).Result);
            var appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            var app = appList.Apps[0];
            apps.HttpContext.SetAppData(new AppData { Id = app.Id, StoreDataId = app.StoreId, Name = app.AppName });
            var vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert
                .IsType<ViewResult>(apps.UpdatePointOfSale(app.Id)).Model);
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
            Assert.IsType<RedirectToActionResult>(apps.UpdatePointOfSale(app.Id, vmpos).Result);
            vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert
                .IsType<ViewResult>(apps.UpdatePointOfSale(app.Id)).Model);
            var publicApps = user.GetController<UIAppsPublicController>();
            var vmview =
                Assert.IsType<ViewPointOfSaleViewModel>(Assert
                    .IsType<ViewResult>(publicApps.ViewPointOfSale(app.Id, PosViewType.Cart).Result).Model);

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
