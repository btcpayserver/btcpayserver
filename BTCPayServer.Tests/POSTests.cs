using System;
using System.Linq;
using System.Net.Http;
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
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;
using static BTCPayServer.Tests.UnitTest1;
using PosViewType = BTCPayServer.Plugins.PointOfSale.PosViewType;

namespace BTCPayServer.Tests
{
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class POSTests(ITestOutputHelper helper) : UnitTestBase(helper)
    {
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


            // Default tax of 8.375%, but 10% for the first item.
            await s.Page.FillAsync("#DefaultTaxRate", "8.375");
            await s.Page.Locator(".template-item").First.ClickAsync();
            await s.Page.Locator("#item-form div").Filter(new() { HasText = "Tax rate %" }).GetByRole(AriaRole.Spinbutton).FillAsync("10");
            await s.Page.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();

            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "App updated");

            // View
            var o = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("#ViewApp");
            await s.SwitchPage(o);
            await s.Page.WaitForSelectorAsync("#PosItems");
            Assert.Empty(await s.Page.QuerySelectorAllAsync("#CartItems tr"));
            var posUrl = s.Page.Url;

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

            await AssertCartSummary(s, new()
            {
                Subtotal = "3,00€",
                Taxes = "0,27 €",
                Total = "3,27 €"
            });

            // Select item with inventory - two of it
            Assert.Equal("5 left", await s.Page.TextContentAsync(".posItem:nth-child(3) .badge.inventory"));
            await s.Page.ClickAsync(".posItem:nth-child(3) .btn-primary");
            await s.Page.ClickAsync(".posItem:nth-child(3) .btn-primary");
            Assert.Equal(3, (await s.Page.QuerySelectorAllAsync("#CartItems tr")).Count);

            await AssertCartSummary(s, new()
            {
                Subtotal = "5,40 €",
                Taxes = "0,47 €",
                Total = "5,87 €"
            });

            // Select items with minimum amount
            await s.Page.ClickAsync(".posItem:nth-child(5) .btn-primary");
            Assert.Equal(4, (await s.Page.QuerySelectorAllAsync("#CartItems tr")).Count);
            await AssertCartSummary(s, new()
            {
                Subtotal = "7,20 €",
                Taxes = "0,62 €",
                Total = "7,82 €"
            });

            // Select items with adjusted minimum amount
            await s.Page.FillAsync(".posItem:nth-child(5) input[name='amount']", "");
            await s.Page.FillAsync(".posItem:nth-child(5) input[name='amount']", "2.3");
            await s.Page.ClickAsync(".posItem:nth-child(5) .btn-primary");
            Assert.Equal(5, (await s.Page.QuerySelectorAllAsync("#CartItems tr")).Count);
            await AssertCartSummary(s, new()
            {
                Subtotal = "9,50 €",
                Taxes = "0,81 €",
                Total = "10,31 €"
            });

            // Select items with custom amount
            await s.Page.FillAsync(".posItem:nth-child(6) input[name='amount']", "");
            await s.Page.FillAsync(".posItem:nth-child(6) input[name='amount']", ".2");
            await s.Page.ClickAsync(".posItem:nth-child(6) .btn-primary");
            Assert.Equal(6, (await s.Page.QuerySelectorAllAsync("#CartItems tr")).Count);
            await AssertCartSummary(s, new()
            {
                Subtotal = "9,70 €",
                Taxes = "0,83 €",
                Total = "10,53 €"
            });

            // Select items with another custom amount
            await s.Page.FillAsync(".posItem:nth-child(6) input[name='amount']", "");
            await s.Page.FillAsync(".posItem:nth-child(6) input[name='amount']", ".3");
            await s.Page.ClickAsync(".posItem:nth-child(6) .btn-primary");
            Assert.Equal(7, (await s.Page.QuerySelectorAllAsync("#CartItems tr")).Count);
            await AssertCartSummary(s, new()
            {
                Subtotal = "10,00 €",
                Taxes = "0,86 €",
                Total = "10,86 €"
            });

            // Discount: 10%
            Assert.False(await s.Page.IsVisibleAsync("#CartDiscount"));
            await s.Page.FillAsync("#Discount", "10");
            await AssertCartSummary(s, new()
            {
                ItemsTotal = "10,00 €",
                Discount = "1,00 € (10%)",
                Subtotal = "9,00 €",
                Taxes = "0,77 €",
                Total = "9,77 €"
            });

            // Tip: 10%
            Assert.False(await s.Page.IsVisibleAsync("#CartTip"));
            await s.Page.ClickAsync("#Tip-10");

            await AssertCartSummary(s, new()
            {
                ItemsTotal = "10,00 €",
                Discount = "1,00 € (10%)",
                Subtotal = "9,00 €",
                Tip = "0,90 € (10%)",
                Taxes = "0,77 €",
                Total = "10,67 €"
            });

            // Check values on checkout page
            await s.Page.ClickAsync("#CartSubmit");
            await s.Page.WaitForSelectorAsync("#Checkout");
            await s.Page.ClickAsync("#DetailsToggle");
            await s.Page.WaitForSelectorAsync("#PaymentDetails-TotalFiat");
            Assert.Contains("0,77 €", await s.Page.TextContentAsync("#PaymentDetails-TaxIncluded"));
            Assert.Contains("10,67 €", await s.Page.TextContentAsync("#PaymentDetails-TotalFiat"));
            //
            // Pay
            await s.PayInvoice(true);


            // Receipt
            await s.Page.ClickAsync("#ReceiptLink");
            await s.Page.WaitForSelectorAsync("#CartData table");
            await AssertReceipt(s, new()
            {
                Items = [
                    new("Black Tea", "2 x 1,00 € = 2,00 €"),
                    new("Green Tea", "1 x 1,00 € = 1,00 €"),
                    new("Rooibos (limited)", "2 x 1,20 € = 2,40 €"),
                    new("Herbal Tea (minimum) (1,80 €)", "1 x 1,80 € = 1,80 €"),
                    new("Herbal Tea (minimum) (2,30 €)", "1 x 2,30 € = 2,30 €"),
                    new("Fruit Tea (any amount) (0,20 €)", "1 x 0,20 € = 0,20 €"),
                    new("Fruit Tea (any amount) (0,30 €)", "1 x 0,30 € = 0,30 €")
                ],
                Sums = [
                    new("Items total", "10,00 €"),
                    new("Discount", "1,00 € (10%)"),
                    new("Subtotal", "9,00 €"),
                    new("Tax", "0,77 €"),
                    new("Tip", "0,90 € (10%)"),
                    new("Total", "10,67 €")
                ]
            });

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

        public class CartSummaryAssertion
        {
            public string Subtotal { get; set; }
            public string Taxes { get; set; }
            public string Total { get; set; }
            public string ItemsTotal { get; set; }
            public string Discount { get; set; }
            public string Tip { get; set; }
        }
        private async Task AssertCartSummary(PlaywrightTester s, CartSummaryAssertion o)
        {
            string[] ids = ["CartItemsTotal", "CartDiscount", "CartAmount", "CartTip", "CartTax", "CartTotal"];
            string[] values = [o.ItemsTotal, o.Discount, o.Subtotal, o.Tip, o.Taxes, o.Total];
            for (int i = 0; i < ids.Length; i++)
            {
                if (values[i] != null)
                {
                    var text = await s.Page.TextContentAsync("#" + ids[i]);
                    Assert.Equal(values[i].NormalizeWhitespaces(), text.NormalizeWhitespaces());
                }
                else
                {
                    Assert.False(await s.Page.IsVisibleAsync("#" + ids[i]));
                }
            }
        }

        [Fact]
        [Trait("Playwright", "Playwright")]
        public async Task CanUsePOSKeypad()
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
            var editUrl = s.Page.Url;
            await s.Page.ClickAsync("label[for='DefaultView_Light']");
            await s.Page.FillAsync("#Currency", "EUR");
            Assert.False(await s.Page.IsCheckedAsync("#EnableTips"));
            await s.Page.ClickAsync("#EnableTips");
            Assert.True(await s.Page.IsCheckedAsync("#EnableTips"));
            await s.Page.FillAsync("#CustomTipPercentages", "");
            await s.Page.FillAsync("#CustomTipPercentages", "10,21");
            Assert.False(await s.Page.IsCheckedAsync("#ShowDiscount"));
            Assert.False(await s.Page.IsCheckedAsync("#ShowItems"));
            await s.Page.ClickAsync("#ShowDiscount");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "App updated");

            // View
            var o = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("#ViewApp");
            await s.SwitchPage(o);

            // basic checks
            var keypadUrl = s.Page.Url;
            await s.Page.WaitForSelectorAsync("#RecentTransactionsToggle");
            Assert.Null(await s.Page.QuerySelectorAsync("#ItemsListToggle"));
            Assert.Contains("EUR", await s.Page.TextContentAsync("#Currency"));
            Assert.Contains("0,00", await s.Page.TextContentAsync("#Amount"));
            Assert.Equal("", await s.Page.TextContentAsync("#Calculation"));
            Assert.True(await s.Page.IsCheckedAsync("#ModeTablist-amounts"));
            Assert.False(await s.Page.IsEnabledAsync("#ModeTablist-discount"));
            Assert.False(await s.Page.IsEnabledAsync("#ModeTablist-tip"));

            // Amount: 1234,56
            await EnterKeypad(s, "123400");
            Assert.Equal("1.234,00", await s.Page.TextContentAsync("#Amount"));
            Assert.Equal("", await s.Page.TextContentAsync("#Calculation"));
            await EnterKeypad(s, "+56");
            Assert.Equal("0,56", await s.Page.TextContentAsync("#Amount"));
            Assert.True(await s.Page.IsEnabledAsync("#ModeTablist-discount"));
            Assert.True(await s.Page.IsEnabledAsync("#ModeTablist-tip"));
            await AssertKeypadCalculation(s, "1.234,00 € + 0,56 € = 1.234,56 €");

            // Discount: 10%
            await s.Page.ClickAsync("label[for='ModeTablist-discount']");
            await EnterKeypad(s, "10");
            Assert.Contains("0,56", await s.Page.TextContentAsync("#Amount"));
            Assert.Contains("10% discount", await s.Page.TextContentAsync("#Discount"));
            await AssertKeypadCalculation(s, "1.234,00 € + 0,56 € - 123,46 € (10%) = 1.111,10 €");

            // Tip: 10%
            await s.Page.ClickAsync("label[for='ModeTablist-tip']");
            await s.Page.ClickAsync("#Tip-10");
            Assert.Contains("0,56", await s.Page.TextContentAsync("#Amount"));
            await AssertKeypadCalculation(s, "1.234,00 € + 0,56 € - 123,46 € (10%) + 111,11 € (10%) = 1.222,21 €");

            // Pay
            await s.Page.ClickAsync("#pay-button");
            await s.Page.ClickAsync("#DetailsToggle");
            Assert.Contains("1 222,21 €", await s.Page.TextContentAsync("#PaymentDetails-TotalFiat"));
            await s.PayInvoice(true);

            // Receipt
            await s.Page.ClickAsync("#ReceiptLink");
            await AssertReceipt(s, new()
            {
                Items = [

                    new("Custom Amount 1", "1 234,00 €"),
                    new("Custom Amount 2", "0,56 €")
                ],
                Sums = [

                    new("Items total", "1 234,56 €"),
                    new("Discount", "123,46 € (10%)"),
                    new("Subtotal", "1 111,10 €"),
                    new("Tip", "111,11 € (10%)"),
                    new("Total", "1 222,21 €")
                ]
            });

            await s.GoToUrl(editUrl);
            await s.Page.ClickAsync("#ShowItems");
            await s.Page.FillAsync("#DefaultTaxRate", "10");
            await s.ClickPagePrimary();
            await s.FindAlertMessage(partialText: "App updated");

            await s.GoToUrl(keypadUrl);
            await s.Page.ClickAsync("#ItemsListToggle");
            await s.Page.WaitForSelectorAsync("#PosItems");
            await s.Page.ClickAsync("#PosItems .posItem--displayed:nth-child(1) .btn-plus");
            await s.Page.ClickAsync("#PosItems .posItem--displayed:nth-child(1) .btn-plus");
            await s.Page.ClickAsync("#PosItems .posItem--displayed:nth-child(2) .btn-plus");
            await s.Page.ClickAsync("#ItemsListOffcanvas button[data-bs-dismiss='offcanvas']");

            await EnterKeypad(s, "123");
            Assert.Contains("1,23", await s.Page.TextContentAsync("#Amount"));
            await AssertKeypadCalculation(s, "2 x Green Tea (1,00 €) = 2,00 € + 1 x Black Tea (1,00 €) = 1,00 € + 1,23 € + 0,42 € (10%) = 4,65 €");

            // Pay
            await s.Page.ClickAsync("#pay-button");
            await s.Page.WaitForSelectorAsync("#Checkout");
            await s.Page.ClickAsync("#DetailsToggle");
            await s.Page.WaitForSelectorAsync("#PaymentDetails-TotalFiat");
            Assert.Contains("4,65 €", await s.Page.TextContentAsync("#PaymentDetails-TotalFiat"));
            await s.PayInvoice(true);


            // Receipt
            await s.Page.ClickAsync("#ReceiptLink");

            await AssertReceipt(s, new()
            {
                Items = [
                    new("Black Tea", "1 x 1,00 € = 1,00 €"),
                    new("Green Tea", "2 x 1,00 € = 2,00 €"),
                    new("Custom Amount 1", "1,23 €")
                ],
                Sums = [
                    new("Subtotal", "4,23 €"),
                    new("Tax", "0,42 € (10%)"),
                    new("Total", "4,65 €")
                ]
            });

            // Guest user can access recent transactions
            await s.GoToHome();
            await s.Logout();
            await s.LogIn(user, userAccount.RegisterDetails.Password);
            await s.GoToUrl(keypadUrl);
            await s.Page.WaitForSelectorAsync("#RecentTransactionsToggle");
            await s.GoToHome();
            await s.Logout();

            // Unauthenticated user can't access recent transactions
            await s.GoToUrl(keypadUrl);
            Assert.False(await s.Page.IsVisibleAsync("#RecentTransactionsToggle"));

            // But they can generate invoices
            await EnterKeypad(s, "123");

            await s.Page.ClickAsync("#pay-button");
            await s.Page.WaitForSelectorAsync("#Checkout");
            await s.Page.ClickAsync("#DetailsToggle");
            await s.Page.WaitForSelectorAsync("#PaymentDetails-TotalFiat");
            Assert.Contains("1,35 €", await s.Page.TextContentAsync("#PaymentDetails-TotalFiat"));
        }

        private static async Task AssertKeypadCalculation(PlaywrightTester s, string expected)
        {
            Assert.Equal(expected.NormalizeWhitespaces(), (await s.Page.TextContentAsync("#Calculation")).NormalizeWhitespaces());
        }

        public class AssertReceiptAssertion
        {
            public record Line(string Key, string Value);
            public Line[] Items { get; set; }
            public Line[] Sums { get; set; }
        }

        private async Task AssertReceipt(PlaywrightTester s, AssertReceiptAssertion assertion)
        {
            await AssertReceipt(s, assertion, "#CartData table tbody tr", "#CartData table tfoot tr");
            // Receipt print
            var o = s.Page.Context.WaitForPageAsync();
            await s.Page.ClickAsync("#ReceiptLinkPrint");
            await using (await s.SwitchPage(o))
            {
                await AssertReceipt(s, assertion, "#PaymentDetails table tr.cart-data", "#PaymentDetails table tr.sums-data");
            }
        }

        private async Task AssertReceipt(PlaywrightTester s, AssertReceiptAssertion assertion, string itemSelector, string sumsSelector)
        {
            var items = await s.Page.QuerySelectorAllAsync(itemSelector);
            var sums = await s.Page.QuerySelectorAllAsync(sumsSelector);
            Assert.Equal(assertion.Items.Length, items.Count);
            Assert.Equal(assertion.Sums.Length, sums.Count);
            for (int i = 0; i < assertion.Items.Length; i++)
            {
                var txt = (await items[i].TextContentAsync()).NormalizeWhitespaces();
                Assert.Contains(assertion.Items[i].Key.NormalizeWhitespaces(), txt);
                Assert.Contains(assertion.Items[i].Value.NormalizeWhitespaces(), txt);
            }

            for (int i = 0; i < assertion.Sums.Length; i++)
            {
                var txt = (await sums[i].TextContentAsync()).NormalizeWhitespaces();
                Assert.Contains(assertion.Sums[i].Key.NormalizeWhitespaces(), txt);
                Assert.Contains(assertion.Sums[i].Value.NormalizeWhitespaces(), txt);
            }
        }

        private async Task EnterKeypad(PlaywrightTester tester, string text)
        {
            foreach (char c in text)
            {
                await tester.Page.ClickAsync($".keypad [data-key='{c}']");
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanUsePoSAppJsonEndpoint()
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
            vmpos.Title = "App POS";
            vmpos.Currency = "EUR";
            Assert.IsType<RedirectToActionResult>(pos.UpdatePointOfSale(app.Id, vmpos).Result);

            // Failing requests
            var (invoiceId1, error1) = await PosJsonRequest(tester, app.Id, "amount=-21&discount=10&tip=2");
            Assert.Null(invoiceId1);
            Assert.Equal("Negative amount is not allowed", error1);
            var (invoiceId2, error2) = await PosJsonRequest(tester, app.Id, "amount=21&discount=-10&tip=-2");
            Assert.Null(invoiceId2);
            Assert.Equal("Negative tip or discount is not allowed", error2);

            // Successful request
            var (invoiceId3, error3) = await PosJsonRequest(tester, app.Id, "amount=21");
            Assert.NotNull(invoiceId3);
            Assert.Null(error3);

            // Check generated invoice
            var invoices = await user.BitPay.GetInvoicesAsync();
            var invoice = invoices.First();
            Assert.Equal(invoiceId3, invoice.Id);
            Assert.Equal(21.00m, invoice.Price);
            Assert.Equal("EUR", invoice.Currency);
        }

        private async Task<(string invoiceId, string error)> PosJsonRequest(ServerTester tester, string appId, string query)
        {
            var uriBuilder = new UriBuilder(tester.PayTester.ServerUri) { Path = $"/apps/{appId}/pos/light", Query = query };
            var request = new HttpRequestMessage(HttpMethod.Post, uriBuilder.Uri);
            request.Headers.Add("Accept", "application/json");
            var response = await tester.PayTester.HttpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            return (json["invoiceId"]?.Value<string>(), json["error"]?.Value<string>());
        }
    }
}
