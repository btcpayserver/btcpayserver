using System;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Plugins.Crowdfund.Controllers;
using BTCPayServer.Plugins.Crowdfund.Models;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using Xunit;
using Xunit.Abstractions;
using static BTCPayServer.Tests.UnitTest1;

namespace BTCPayServer.Tests
{
    public class CrowdfundTests : UnitTestBase
    {
        public CrowdfundTests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanCreateAndDeleteCrowdfundApp()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            var user2 = tester.NewAccount();
            await user2.GrantAccessAsync();
            var stores = user.GetController<UIStoresController>();
            var apps = user.GetController<UIAppsController>();
            var apps2 = user2.GetController<UIAppsController>();
            var crowdfund = user.GetController<UICrowdfundController>();
            var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp(user.StoreId)).Model);
            var appType = AppType.Crowdfund.ToString();
            Assert.NotNull(vm.SelectedAppType);
            Assert.Null(vm.AppName);
            vm.AppName = "test";
            vm.SelectedAppType = appType;
            var redirectToAction = Assert.IsType<RedirectToActionResult>(apps.CreateApp(user.StoreId, vm).Result);
            Assert.Equal(nameof(crowdfund.UpdateCrowdfund), redirectToAction.ActionName);
            var appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            var app = appList.Apps[0];
            var appData = new AppData { Id = app.Id, StoreDataId = app.StoreId, Name = app.AppName, AppType = appType };
            apps.HttpContext.SetAppData(appData);
            crowdfund.HttpContext.SetAppData(appData);
            var appList2 =
                Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps2.ListApps(user2.StoreId).Result).Model);
            Assert.Single(appList.Apps);
            Assert.Empty(appList2.Apps);
            Assert.Equal("test", appList.Apps[0].AppName);
            Assert.Equal(apps.CreatedAppId, appList.Apps[0].Id);
            Assert.True(appList.Apps[0].IsOwner);
            Assert.Equal(user.StoreId, appList.Apps[0].StoreId);
            Assert.IsType<NotFoundResult>(apps2.DeleteApp(appList.Apps[0].Id));
            Assert.IsType<ViewResult>(apps.DeleteApp(appList.Apps[0].Id));
            redirectToAction = Assert.IsType<RedirectToActionResult>(apps.DeleteAppPost(appList.Apps[0].Id).Result);
            Assert.Equal(nameof(stores.Dashboard), redirectToAction.ActionName);
            appList = await apps.ListApps(user.StoreId).AssertViewModelAsync<ListAppsViewModel>();
            Assert.Empty(appList.Apps);
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanContributeOnlyWhenAllowed()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");
            var apps = user.GetController<UIAppsController>();
            var crowdfund = user.GetController<UICrowdfundController>();
            var vm = apps.CreateApp(user.StoreId).AssertViewModel<CreateAppViewModel>();
            var appType = AppType.Crowdfund.ToString();
            vm.AppName = "test";
            vm.SelectedAppType = appType;
            Assert.IsType<RedirectToActionResult>(apps.CreateApp(user.StoreId, vm).Result);
            var appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            var app = appList.Apps[0];
            var appData = new AppData { Id = app.Id, StoreDataId = app.StoreId, Name = app.AppName, AppType = appType };
            apps.HttpContext.SetAppData(appData);
            crowdfund.HttpContext.SetAppData(appData);

            //Scenario 1: Not Enabled - Not Allowed
            var crowdfundViewModel = await crowdfund.UpdateCrowdfund(app.Id).AssertViewModelAsync<UpdateCrowdfundViewModel>();
            crowdfundViewModel.TargetCurrency = "BTC";
            crowdfundViewModel.Enabled = false;
            crowdfundViewModel.EndDate = null;

            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel, "save").Result);

            var anonAppPubsController = tester.PayTester.GetController<UICrowdfundController>();
            var crowdfundController = user.GetController<UICrowdfundController>();

            Assert.IsType<NotFoundObjectResult>(await anonAppPubsController.ContributeToCrowdfund(app.Id, new ContributeToCrowdfund()
            {
                Amount = new decimal(0.01)
            }, default));

            Assert.IsType<NotFoundResult>(await anonAppPubsController.ViewCrowdfund(app.Id, string.Empty));

            //Scenario 2: Not Enabled But Admin - Allowed
            Assert.IsType<OkObjectResult>(await crowdfundController.ContributeToCrowdfund(app.Id, new ContributeToCrowdfund()
            {
                RedirectToCheckout = false,
                Amount = new decimal(0.01)
            }, default));
            Assert.IsType<ViewResult>(await crowdfundController.ViewCrowdfund(app.Id, string.Empty));
            Assert.IsType<NotFoundResult>(await anonAppPubsController.ViewCrowdfund(app.Id, string.Empty));

            //Scenario 3: Enabled But Start Date > Now - Not Allowed
            crowdfundViewModel.StartDate = DateTime.Today.AddDays(2);
            crowdfundViewModel.Enabled = true;

            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel, "save").Result);
            Assert.IsType<NotFoundObjectResult>(await anonAppPubsController.ContributeToCrowdfund(app.Id, new ContributeToCrowdfund()
            {
                Amount = new decimal(0.01)
            }, default));

            //Scenario 4: Enabled But End Date < Now - Not Allowed
            crowdfundViewModel.StartDate = DateTime.Today.AddDays(-2);
            crowdfundViewModel.EndDate = DateTime.Today.AddDays(-1);
            crowdfundViewModel.Enabled = true;

            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel, "save").Result);
            Assert.IsType<NotFoundObjectResult>(await anonAppPubsController.ContributeToCrowdfund(app.Id, new ContributeToCrowdfund()
            {
                Amount = new decimal(0.01)
            }, default));

            //Scenario 5: Enabled and within correct timeframe, however target is enforced and Amount is Over - Not Allowed
            crowdfundViewModel.StartDate = DateTime.Today.AddDays(-2);
            crowdfundViewModel.EndDate = DateTime.Today.AddDays(2);
            crowdfundViewModel.Enabled = true;
            crowdfundViewModel.TargetAmount = 1;
            crowdfundViewModel.TargetCurrency = "BTC";
            crowdfundViewModel.EnforceTargetAmount = true;
            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel, "save").Result);
            Assert.IsType<NotFoundObjectResult>(await anonAppPubsController.ContributeToCrowdfund(app.Id, new ContributeToCrowdfund()
            {
                Amount = new decimal(1.01)
            }, default));

            //Scenario 6: Allowed
            Assert.IsType<OkObjectResult>(await anonAppPubsController.ContributeToCrowdfund(app.Id, new ContributeToCrowdfund()
            {
                Amount = new decimal(0.05)
            }, default));
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanComputeCrowdfundModel()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");
            await user.SetNetworkFeeMode(NetworkFeeMode.Never);
            var apps = user.GetController<UIAppsController>();
            var crowdfund = user.GetController<UICrowdfundController>();
            var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp(user.StoreId)).Model);
            var appType = AppType.Crowdfund.ToString();
            vm.AppName = "test";
            vm.SelectedAppType = appType;
            Assert.IsType<RedirectToActionResult>(apps.CreateApp(user.StoreId, vm).Result);
            var appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            var app = appList.Apps[0];
            var appData = new AppData { Id = app.Id, StoreDataId = app.StoreId, Name = app.AppName, AppType = appType };
            apps.HttpContext.SetAppData(appData);
            crowdfund.HttpContext.SetAppData(appData);

            TestLogs.LogInformation("We create an invoice with a hardcap");
            var crowdfundViewModel = await crowdfund.UpdateCrowdfund(app.Id).AssertViewModelAsync<UpdateCrowdfundViewModel>();
            crowdfundViewModel.Enabled = true;
            crowdfundViewModel.EndDate = null;
            crowdfundViewModel.TargetAmount = 100;
            crowdfundViewModel.TargetCurrency = "BTC";
            crowdfundViewModel.UseAllStoreInvoices = true;
            crowdfundViewModel.EnforceTargetAmount = true;
            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel, "save").Result);

            var publicApps = user.GetController<UICrowdfundController>();

            var model = Assert.IsType<ViewCrowdfundViewModel>(Assert
                .IsType<ViewResult>(publicApps.ViewCrowdfund(app.Id, String.Empty).Result).Model);

            Assert.Equal(crowdfundViewModel.TargetAmount, model.TargetAmount);
            Assert.Equal(crowdfundViewModel.EndDate, model.EndDate);
            Assert.Equal(crowdfundViewModel.StartDate, model.StartDate);
            Assert.Equal(crowdfundViewModel.TargetCurrency, model.TargetCurrency);
            Assert.Equal(0m, model.Info.CurrentAmount);
            Assert.Equal(0m, model.Info.CurrentPendingAmount);
            Assert.Equal(0m, model.Info.ProgressPercentage);

            TestLogs.LogInformation("Unpaid invoices should show as pending contribution because it is hardcap");
            TestLogs.LogInformation("Because UseAllStoreInvoices is true, we can manually create an invoice and it should show as contribution");
            var invoice = await user.BitPay.CreateInvoiceAsync(new Invoice
            {
                Buyer = new Buyer() { email = "test@fwf.com" },
                Price = 1m,
                Currency = "BTC",
                PosData = "posData",
                ItemDesc = "Some description",
                TransactionSpeed = "high",
                FullNotifications = true
            }, Facade.Merchant);

            model = Assert.IsType<ViewCrowdfundViewModel>(Assert
                .IsType<ViewResult>(publicApps.ViewCrowdfund(app.Id, string.Empty).Result).Model);

            Assert.Equal(0m, model.Info.CurrentAmount);
            Assert.Equal(1m, model.Info.CurrentPendingAmount);
            Assert.Equal(0m, model.Info.ProgressPercentage);
            Assert.Equal(1m, model.Info.PendingProgressPercentage);

            TestLogs.LogInformation("Let's check current amount change once payment is confirmed");
            var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, tester.ExplorerNode.Network);
            tester.ExplorerNode.SendToAddress(invoiceAddress, invoice.BtcDue);
            tester.ExplorerNode.Generate(1); // By default invoice confirmed at 1 block
            TestUtils.Eventually(() =>
            {
                model = Assert.IsType<ViewCrowdfundViewModel>(Assert
                .IsType<ViewResult>(publicApps.ViewCrowdfund(app.Id, String.Empty).Result).Model);
                Assert.Equal(1m, model.Info.CurrentAmount);
                Assert.Equal(0m, model.Info.CurrentPendingAmount);
            });

            TestLogs.LogInformation("Because UseAllStoreInvoices is true, let's make sure the invoice is tagged");
            var invoiceEntity = tester.PayTester.InvoiceRepository.GetInvoice(invoice.Id).GetAwaiter().GetResult();
            Assert.True(invoiceEntity.Version >= InvoiceEntity.InternalTagSupport_Version);
            Assert.Contains(AppService.GetAppInternalTag(app.Id), invoiceEntity.InternalTags);

            crowdfundViewModel.UseAllStoreInvoices = false;
            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel, "save").Result);

            TestLogs.LogInformation("Because UseAllStoreInvoices is false, let's make sure the invoice is not tagged");
            invoice = await user.BitPay.CreateInvoiceAsync(new Invoice
            {
                Buyer = new Buyer { email = "test@fwf.com" },
                Price = 1m,
                Currency = "BTC",
                PosData = "posData",
                ItemDesc = "Some description",
                TransactionSpeed = "high",
                FullNotifications = true
            }, Facade.Merchant);
            invoiceEntity = tester.PayTester.InvoiceRepository.GetInvoice(invoice.Id).GetAwaiter().GetResult();
            Assert.DoesNotContain(AppService.GetAppInternalTag(app.Id), invoiceEntity.InternalTags);

            TestLogs.LogInformation("After turning setting a softcap, let's check that only actual payments are counted");
            crowdfundViewModel.EnforceTargetAmount = false;
            crowdfundViewModel.UseAllStoreInvoices = true;
            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel, "save").Result);
            invoice = await user.BitPay.CreateInvoiceAsync(new Invoice
            {
                Buyer = new Buyer { email = "test@fwf.com" },
                Price = 1m,
                Currency = "BTC",
                PosData = "posData",
                ItemDesc = "Some description",
                TransactionSpeed = "high",
                FullNotifications = true
            }, Facade.Merchant);
            Assert.Equal(0m, model.Info.CurrentPendingAmount);
            invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, tester.ExplorerNode.Network);
            await tester.ExplorerNode.SendToAddressAsync(invoiceAddress, Money.Coins(0.5m));
            await tester.ExplorerNode.SendToAddressAsync(invoiceAddress, Money.Coins(0.2m));
            TestUtils.Eventually(() =>
            {
                model = Assert.IsType<ViewCrowdfundViewModel>(Assert
                .IsType<ViewResult>(publicApps.ViewCrowdfund(app.Id, string.Empty).Result).Model);
                Assert.Equal(0.7m, model.Info.CurrentPendingAmount);
            });
        }
    }
}
