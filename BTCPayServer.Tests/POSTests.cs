using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Hosting;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Plugins.Crowdfund.Models;
using BTCPayServer.Plugins.PointOfSale;
using BTCPayServer.Plugins.PointOfSale.Controllers;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Services.Apps;
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

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanParseOldYmlCorrectly()
        {
              var testOriginalDefaultYmlTemplate = @"
green tea:
  price: 1
  title: Green Tea
  description:  Lovely, fresh and tender, Meng Ding Gan Lu ('sweet dew') is grown in the lush Meng Ding Mountains of the southwestern province of Sichuan where it has been cultivated for over a thousand years.
  image: ~/img/pos-sample/green-tea.jpg

black tea:
  price: 1
  title: Black Tea
  description: Tian Jian Tian Jian means 'heavenly tippy tea' in Chinese, and it describes the finest grade of dark tea. Our Tian Jian dark tea is from Hunan province which is famous for making some of the best dark teas available.
  image: ~/img/pos-sample/black-tea.jpg

rooibos:
  price: 1.2
  title: Rooibos
  description: Rooibos is a dramatic red tea made from a South African herb that contains polyphenols and flavonoids. Often called 'African redbush tea', Rooibos herbal tea delights the senses and delivers potential health benefits with each caffeine-free sip.
  image: ~/img/pos-sample/rooibos.jpg

pu erh:
  price: 2
  title: Pu Erh
  description: This loose pur-erh tea is produced in Yunnan Province, China. The process in a relatively high humidity environment has mellowed the elemental character of the tea when compared to young Pu-erh.
  image: ~/img/pos-sample/pu-erh.jpg

herbal tea:
  price: 1.8
  title: Herbal Tea
  description: Chamomile tea is made from the flower heads of the chamomile plant. The medicinal use of chamomile dates back to the ancient Egyptians, Romans and Greeks. Pay us what you want!
  image: ~/img/pos-sample/herbal-tea.jpg
  custom: true

fruit tea:
  price: 1.5
  title: Fruit Tea
  description: The Tibetan Himalayas, the land is majestic and beautiful—a spiritual place where, despite the perilous environment, many journey seeking enlightenment. Pay us what you want!
  image: ~/img/pos-sample/fruit-tea.jpg
  inventory: 5
  custom: true
";
        var parsedDefault =     MigrationStartupTask.ParsePOSYML(testOriginalDefaultYmlTemplate);
        Assert.Equal(6, parsedDefault.Length);
        Assert.Equal( "Green Tea" ,parsedDefault[0].Title);
        Assert.Equal( "green tea" ,parsedDefault[0].Id);
        Assert.Equal( "Lovely, fresh and tender, Meng Ding Gan Lu ('sweet dew') is grown in the lush Meng Ding Mountains of the southwestern province of Sichuan where it has been cultivated for over a thousand years." ,parsedDefault[0].Description);
        Assert.Null( parsedDefault[0].BuyButtonText);
        Assert.Equal( "~/img/pos-sample/green-tea.jpg" ,parsedDefault[0].Image);
        Assert.Equal( 1 ,parsedDefault[0].Price);
        Assert.Equal( ViewPointOfSaleViewModel.ItemPriceType.Fixed ,parsedDefault[0].PriceType);
        Assert.Null( parsedDefault[0].AdditionalData);
        Assert.Null( parsedDefault[0].PaymentMethods);
        
        
        Assert.Equal( "Herbal Tea" ,parsedDefault[4].Title);
        Assert.Equal( "herbal tea" ,parsedDefault[4].Id);
        Assert.Equal( "Chamomile tea is made from the flower heads of the chamomile plant. The medicinal use of chamomile dates back to the ancient Egyptians, Romans and Greeks. Pay us what you want!" ,parsedDefault[4].Description);
        Assert.Null( parsedDefault[4].BuyButtonText);
        Assert.Equal( "~/img/pos-sample/herbal-tea.jpg" ,parsedDefault[4].Image);
        Assert.Equal( 1.8m ,parsedDefault[4].Price);
        Assert.Equal( ViewPointOfSaleViewModel.ItemPriceType.Minimum ,parsedDefault[4].PriceType);
        Assert.Null( parsedDefault[4].AdditionalData);
        Assert.Null( parsedDefault[4].PaymentMethods);
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
            var appType = PointOfSaleAppType.AppType;
            vm.AppName = "test";
            vm.SelectedAppType = appType;
            var redirect = Assert.IsType<RedirectResult>(apps.CreateApp(user.StoreId, vm).Result);
            Assert.EndsWith("/settings/pos", redirect.Url);
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
            vmpos.Currency = "EUR";
            vmpos.Template = AppService.SerializeTemplate(MigrationStartupTask.ParsePOSYML(vmpos.Template));
            Assert.IsType<RedirectToActionResult>(pos.UpdatePointOfSale(app.Id, vmpos).Result);
            await pos.UpdatePointOfSale(app.Id).AssertViewModelAsync<UpdatePointOfSaleViewModel>();
            var publicApps = user.GetController<UIPointOfSaleController>();
            var vmview = await publicApps.ViewPointOfSale(app.Id, PosViewType.Cart).AssertViewModelAsync<ViewPointOfSaleViewModel>();

            Assert.Equal("EUR", vmview.CurrencyCode);
            // apple shouldn't be available since we it's set to "disabled: true" above
            Assert.Equal(2, vmview.Items.Length);
            Assert.Equal("orange", vmview.Items[0].Title);
            Assert.Equal("donation", vmview.Items[1].Title);
            // orange is available
            Assert.IsType<RedirectToActionResult>(publicApps
                .ViewPointOfSale(app.Id, PosViewType.Cart, 0, choiceKey: "orange").Result);
            // apple is not found
            Assert.IsType<NotFoundResult>(publicApps
                .ViewPointOfSale(app.Id, PosViewType.Cart, 0, choiceKey: "apple").Result);
            
            // List
            appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            app = appList.Apps[0];
            apps = user.GetController<UIAppsController>();
            appData = new AppData { Id = app.Id, StoreDataId = app.StoreId, Name = app.AppName, AppType = appType, Settings = "{\"currency\":\"EUR\"}" };
            apps.HttpContext.SetAppData(appData);
            pos.HttpContext.SetAppData(appData);
            Assert.Single(appList.Apps);
            Assert.Equal("test", app.AppName);
            Assert.True(app.Role.ToPermissionSet(appList.Apps[0].StoreId).Contains(Policies.CanModifyStoreSettings, app.StoreId));
            Assert.Equal(user.StoreId, app.StoreId);
            Assert.False(app.Archived);
            // Archive
            redirect = Assert.IsType<RedirectResult>(apps.ToggleArchive(app.Id).Result);
            Assert.EndsWith("/settings/pos", redirect.Url);
            appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            Assert.Empty(appList.Apps);
            appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId, archived: true).Result).Model);
            app = appList.Apps[0];
            Assert.True(app.Archived);
            Assert.IsType<NotFoundResult>(await publicApps.ViewPointOfSale(app.Id, PosViewType.Static));
            // Unarchive
            redirect = Assert.IsType<RedirectResult>(apps.ToggleArchive(app.Id).Result);
            Assert.EndsWith("/settings/pos", redirect.Url);
            appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            app = appList.Apps[0];
            Assert.False(app.Archived);
            Assert.IsType<ViewResult>(await publicApps.ViewPointOfSale(app.Id, PosViewType.Static));
            // Delete
            Assert.IsType<ViewResult>(apps.DeleteApp(app.Id));
            var redirectToAction = Assert.IsType<RedirectToActionResult>(apps.DeleteAppPost(app.Id).Result);
            Assert.Equal(nameof(UIStoresController.Dashboard), redirectToAction.ActionName);
            appList = await apps.ListApps(user.StoreId).AssertViewModelAsync<ListAppsViewModel>();
            Assert.Empty(appList.Apps);
        }
    }
}
