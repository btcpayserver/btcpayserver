using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Services.Apps;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using Xunit.Abstractions;
using static BTCPayServer.Tests.UnitTest1;

namespace BTCPayServer.Tests
{
    public class POSTests
    {
        public POSTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        public async Task CanUsePoSApp1()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                var apps = user.GetController<AppsController>();
                var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp().Result).Model);
                vm.AppName = "test";
                vm.SelectedAppType = AppType.PointOfSale.ToString();
                Assert.IsType<RedirectToActionResult>(apps.CreateApp(vm).Result);
                var appId = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps().Result).Model)
                    .Apps[0].Id;
                var vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert
                    .IsType<ViewResult>(apps.UpdatePointOfSale(appId).Result).Model);
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
                Assert.IsType<RedirectToActionResult>(apps.UpdatePointOfSale(appId, vmpos).Result);
                vmpos = Assert.IsType<UpdatePointOfSaleViewModel>(Assert
                    .IsType<ViewResult>(apps.UpdatePointOfSale(appId).Result).Model);
                var publicApps = user.GetController<AppsPublicController>();
                var vmview =
                    Assert.IsType<ViewPointOfSaleViewModel>(Assert
                        .IsType<ViewResult>(publicApps.ViewPointOfSale(appId, PosViewType.Cart).Result).Model);

                // apple shouldn't be available since we it's set to "disabled: true" above
                Assert.Equal(2, vmview.Items.Length);
                Assert.Equal("orange", vmview.Items[0].Title);
                Assert.Equal("donation", vmview.Items[1].Title);
                // orange is available
                Assert.IsType<RedirectToActionResult>(publicApps
                    .ViewPointOfSale(appId, PosViewType.Cart, 0, null, null, null, null, "orange").Result);
                // apple is not found
                Assert.IsType<NotFoundResult>(publicApps
                    .ViewPointOfSale(appId, PosViewType.Cart, 0, null, null, null, null, "apple").Result);
            }
        }
    }
}
