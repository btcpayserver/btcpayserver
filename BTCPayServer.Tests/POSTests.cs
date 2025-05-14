using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Hosting;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Plugins.PointOfSale;
using BTCPayServer.Plugins.PointOfSale.Controllers;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Mvc;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;
using static BTCPayServer.Tests.UnitTest1;
using PosViewType = BTCPayServer.Plugins.PointOfSale.PosViewType;

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
        Assert.Equal( AppItemPriceType.Fixed ,parsedDefault[0].PriceType);
        Assert.Null( parsedDefault[0].AdditionalData);


        Assert.Equal( "Herbal Tea" ,parsedDefault[4].Title);
        Assert.Equal( "herbal tea" ,parsedDefault[4].Id);
        Assert.Equal( "Chamomile tea is made from the flower heads of the chamomile plant. The medicinal use of chamomile dates back to the ancient Egyptians, Romans and Greeks. Pay us what you want!" ,parsedDefault[4].Description);
        Assert.Null( parsedDefault[4].BuyButtonText);
        Assert.Equal( "~/img/pos-sample/herbal-tea.jpg" ,parsedDefault[4].Image);
        Assert.Equal( 1.8m ,parsedDefault[4].Price);
        Assert.Equal( AppItemPriceType.Minimum ,parsedDefault[4].PriceType);
        Assert.Null( parsedDefault[4].AdditionalData);
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanParseAppTemplate()
        {
            var template = @"[
              {
                ""description"": ""Lovely, fresh and tender, Meng Ding Gan Lu ('sweet dew') is grown in the lush Meng Ding Mountains of the southwestern province of Sichuan where it has been cultivated for over a thousand years."",
                ""id"": ""green-tea"",
                ""image"": ""~/img/pos-sample/green-tea.jpg"",
                ""priceType"": ""Fixed"",
                ""price"": ""1"",
                ""title"": ""Green Tea"",
                ""disabled"": false
              },
              {
                ""description"": ""Tian Jian Tian Jian means 'heavenly tippy tea' in Chinese, and it describes the finest grade of dark tea. Our Tian Jian dark tea is from Hunan province which is famous for making some of the best dark teas available."",
                ""id"": ""black-tea"",
                ""image"": ""~/img/pos-sample/black-tea.jpg"",
                ""priceType"": ""Fixed"",
                ""price"": ""1"",
                ""title"": ""Black Tea"",
                ""disabled"": false
              }
            ]";

            var items = AppService.Parse(template);
            Assert.Equal(2, items.Length);
            Assert.Equal("green-tea", items[0].Id);
            Assert.Equal("black-tea", items[1].Id);

            // Fails gracefully for missing ID
            var missingId = template.Replace(@"""id"": ""green-tea"",", "");
            items = AppService.Parse(missingId);
            Assert.Single(items);
            Assert.Equal("black-tea", items[0].Id);

            // Throws for missing ID
            Assert.Throws<ArgumentException>(() => AppService.Parse(missingId, true, true));

            // Fails gracefully for duplicate IDs
            var duplicateId = template.Replace(@"""id"": ""green-tea"",", @"""id"": ""black-tea"",");
            items = AppService.Parse(duplicateId);
            Assert.Empty(items);

            // Throws for duplicate IDs
            Assert.Throws<ArgumentException>(() => AppService.Parse(duplicateId, true, true));
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

        [Fact]
        [Trait("Playwright", "Playwright")]
        public async Task CanUsePOSCart()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();

            // Create users
            var user = await s.RegisterNewUser();
            var userAccount = s.AsTestAccount();
            await s.GoToHome();
            await s.Logout();
            await s.GoToRegister();
            await s.RegisterNewUser(true);

            // Setup store and associate user
            (_, string storeId) = await s.CreateNewStore();
            await s.GoToStore();
            await s.AddDerivationScheme();
            await s.AddUserToStore(storeId, user, "Guest");

            // Setup POS
            await s.CreateApp("PointOfSale");
            await s.Page.ClickAsync("label[for='DefaultView_Cart']");
            await s.Page.FillAsync("#Currency", "EUR");
            Assert.False(await s.Page.IsCheckedAsync("#EnableTips"));
            await s.Page.ClickAsync("#EnableTips");
            Assert.True(await s.Page.IsCheckedAsync("#EnableTips"));
            await s.Page.FillAsync("#CustomTipPercentages", "10,21");
            Assert.False(await s.Page.IsCheckedAsync("#ShowDiscount"));
            await s.Page.ClickAsync("#ShowDiscount");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "App updated");
            //
            // View
            var o = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("#ViewApp");
            var newPage = await o;
            await newPage.WaitForSelectorAsync("#PosItems");
            Assert.Empty(await newPage.QuerySelectorAllAsync("#CartItems tr"));
            var posUrl = newPage.Url;
            await newPage.BringToFrontAsync();
            s.Page = newPage;

            // Select and clear
            await s.Page.ClickAsync(".posItem:nth-child(1) .btn-primary");
            Assert.Single(await s.Page.QuerySelectorAllAsync("#CartItems tr"));
            await s.Page.ClickAsync("#CartClear");
            Assert.Empty(await s.Page.QuerySelectorAllAsync("#CartItems tr"));

            // Select simple items
            await s.Page.ClickAsync(".posItem:nth-child(1) .btn-primary");
            Assert.Single(await s.Page.QuerySelectorAllAsync("#CartItems tr"));
            await s.Page.ClickAsync(".posItem:nth-child(2) .btn-primary");
            await s.Page.ClickAsync(".posItem:nth-child(2) .btn-primary");
            Assert.Equal(2, (await s.Page.QuerySelectorAllAsync("#CartItems tr")).Count);
            var t = await s.Page.TextContentAsync("#CartTotal");
            Assert.Equal("3,00 €", await s.Page.TextContentAsync("#CartTotal"));

            // Select item with inventory - two of it
            Assert.Equal("5 left", await s.Page.TextContentAsync(".posItem:nth-child(3) .badge.inventory"));
            await s.Page.ClickAsync(".posItem:nth-child(3) .btn-primary");
            await s.Page.ClickAsync(".posItem:nth-child(3) .btn-primary");
            Assert.Equal(3, (await s.Page.QuerySelectorAllAsync("#CartItems tr")).Count);
            Assert.Equal("5,40 €", await s.Page.TextContentAsync("#CartTotal"));

            // Select items with minimum amount
            await s.Page.ClickAsync(".posItem:nth-child(5) .btn-primary");
            Assert.Equal(4, (await s.Page.QuerySelectorAllAsync("#CartItems tr")).Count);
            Assert.Equal("7,20 €", await s.Page.TextContentAsync("#CartTotal"));

            // Select items with adjusted minimum amount
            await s.Page.FillAsync(".posItem:nth-child(5) input[name='amount']", "");
            await s.Page.FillAsync(".posItem:nth-child(5) input[name='amount']", "2.3");
            await s.Page.ClickAsync(".posItem:nth-child(5) .btn-primary");
            Assert.Equal(5, (await s.Page.QuerySelectorAllAsync("#CartItems tr")).Count);
            Assert.Equal("9,50 €", await s.Page.TextContentAsync("#CartTotal"));

            // Select items with custom amount
            await s.Page.FillAsync(".posItem:nth-child(6) input[name='amount']", "");
            await s.Page.FillAsync(".posItem:nth-child(6) input[name='amount']", ".2");
            await s.Page.ClickAsync(".posItem:nth-child(6) .btn-primary");
            Assert.Equal(6, (await s.Page.QuerySelectorAllAsync("#CartItems tr")).Count);
            Assert.Equal("9,70 €", await s.Page.TextContentAsync("#CartTotal"));

            // Select items with another custom amount
            await s.Page.FillAsync(".posItem:nth-child(6) input[name='amount']", "");
            await s.Page.FillAsync(".posItem:nth-child(6) input[name='amount']", ".3");
            await s.Page.ClickAsync(".posItem:nth-child(6) .btn-primary");
            Assert.Equal(7, (await s.Page.QuerySelectorAllAsync("#CartItems tr")).Count);
            Assert.Equal("10,00 €", await s.Page.TextContentAsync("#CartTotal"));

            // Discount: 10%
            Assert.False(await s.Page.IsVisibleAsync("#CartDiscount"));
            await s.Page.FillAsync("#Discount", "10");
            Assert.Contains("10% = 1,00 €".NormalizeWhitespaces(), (await s.Page.TextContentAsync("#CartDiscount")).NormalizeWhitespaces());
            Assert.Equal("9,00 €", await s.Page.TextContentAsync("#CartTotal"));

            // Tip: 10%
            Assert.False(await s.Page.IsVisibleAsync("#CartTip"));
            await s.Page.ClickAsync("#Tip-10");
            Assert.Contains("10% = 0,90 €".NormalizeWhitespaces(), (await s.Page.TextContentAsync("#CartTip")).NormalizeWhitespaces());
            Assert.Equal("9,90 €", await s.Page.TextContentAsync("#CartTotal"));

            // Check values on checkout page
            await s.Page.ClickAsync("#CartSubmit");
            await s.Page.WaitForSelectorAsync("#Checkout");
            await s.Page.ClickAsync("#DetailsToggle");
            await s.Page.WaitForSelectorAsync("#PaymentDetails-TotalFiat");
            Assert.Contains("9,90 €", await s.Page.TextContentAsync("#PaymentDetails-TotalFiat"));
            //
            // Pay
            await s.PayInvoice(true);


            // Receipt
            await s.Page.ClickAsync("#ReceiptLink");
            await s.Page.WaitForSelectorAsync("#CartData table");
            var items = await s.Page.QuerySelectorAllAsync("#CartData table tbody tr");
            var sums = await s.Page.QuerySelectorAllAsync("#CartData table tfoot tr");
            Assert.Equal(7, items.Count);
            Assert.Equal(5, sums.Count);
            Assert.Contains("Black Tea", await items[0].TextContentAsync());
            Assert.Contains("2 x 1,00 € = 2,00 €", await items[0].TextContentAsync());
            Assert.Contains("Green Tea", await items[1].TextContentAsync());
            Assert.Contains("1 x 1,00 € = 1,00 €", await items[1].TextContentAsync());
            Assert.Contains("Rooibos (limited)", await items[2].TextContentAsync());
            Assert.Contains("2 x 1,20 € = 2,40 €", await items[2].TextContentAsync());
            Assert.Contains("Herbal Tea (minimum) (1,80 €)", await items[3].TextContentAsync());
            Assert.Contains("1 x 1,80 € = 1,80 €", await items[3].TextContentAsync());
            Assert.Contains("Herbal Tea (minimum) (2,30 €)", await items[4].TextContentAsync());
            Assert.Contains("1 x 2,30 € = 2,30 €", await items[4].TextContentAsync());
            Assert.Contains("Fruit Tea (any amount) (0,20 €)", await items[5].TextContentAsync());
            Assert.Contains("1 x 0,20 € = 0,20 €", await items[5].TextContentAsync());
            Assert.Contains("Fruit Tea (any amount) (0,30 €)", await items[6].TextContentAsync());
            Assert.Contains("1 x 0,30 € = 0,30 €", await items[6].TextContentAsync());
            Assert.Contains("Items total", await sums[0].TextContentAsync());
            Assert.Contains("10,00 €", await sums[0].TextContentAsync());
            Assert.Contains("Discount", await sums[1].TextContentAsync());
            Assert.Contains("10% = 1,00 €", await sums[1].TextContentAsync());
            Assert.Contains("Subtotal", await sums[2].TextContentAsync());
            var aaa = await sums[2].TextContentAsync();
            Assert.Contains("9,00 €", await sums[2].TextContentAsync());
            Assert.Contains("Tip", await sums[3].TextContentAsync());
            Assert.Contains("10% = 0,90 €", await sums[3].TextContentAsync());
            Assert.Contains("Total", await sums[4].TextContentAsync());
            Assert.Contains("9,90 €", await sums[4].TextContentAsync());

            // Check inventory got updated and is now 3 instead of 5
            await s.GoToUrl(posUrl);
            Assert.Equal("3 left", await s.Page.TextContentAsync(".posItem:nth-child(3) .badge.inventory"));

            // Guest user can access recent transactions
            await s.GoToHome();
            await s.Logout();
            await s.LogIn(user, userAccount.RegisterDetails.Password);
            await s.GoToUrl(posUrl);
            await s.Page.WaitForSelectorAsync("#RecentTransactionsToggle");
            await s.GoToHome();
            await s.Logout();

            // Unauthenticated user can't access recent transactions
            await s.GoToUrl(posUrl);
            Assert.False(await s.Page.IsVisibleAsync("#RecentTransactionsToggle"));
        }
    }
}
