using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Forms;
using BTCPayServer.Forms.Models;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Plugins.Crowdfund;
using BTCPayServer.Plugins.Crowdfund.Controllers;
using BTCPayServer.Plugins.Crowdfund.Models;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Http.HttpResults;
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
            await user.RegisterDerivationSchemeAsync("BTC");
            await user2.RegisterDerivationSchemeAsync("BTC");
            var apps = user.GetController<UIAppsController>();
            var apps2 = user2.GetController<UIAppsController>();
            var crowdfund = user.GetController<UICrowdfundController>();
            var appType = CrowdfundAppType.AppType;
            var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp(user.StoreId, appType)).Model);
            Assert.Equal(appType, vm.SelectedAppType);
            Assert.Null(vm.AppName);
            vm.AppName = "test";
            var redirect = Assert.IsType<RedirectResult>(apps.CreateApp(user.StoreId, vm).Result);
            Assert.EndsWith("/settings/crowdfund", redirect.Url);
            var appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            var app = appList.Apps[0];
            var appData = new AppData { Id = app.Id, StoreDataId = app.StoreId, Name = app.AppName, AppType = appType };
            apps.HttpContext.SetAppData(appData);
            crowdfund.HttpContext.SetAppData(appData);
            var appList2 =
                Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps2.ListApps(user2.StoreId).Result).Model);
            Assert.Single(appList.Apps);
            Assert.Empty(appList2.Apps);
            Assert.Equal("test", app.AppName);
            Assert.Equal(apps.CreatedAppId, app.Id);
            Assert.True(app.Role.ToPermissionSet(app.StoreId).Contains(Policies.CanModifyStoreSettings, app.StoreId));
            Assert.Equal(user.StoreId, app.StoreId);
            // Archive
            redirect = Assert.IsType<RedirectResult>(apps.ToggleArchive(app.Id).Result);
            Assert.EndsWith("/settings/crowdfund", redirect.Url);
            appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            Assert.Empty(appList.Apps);
            appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId, archived: true).Result).Model);
            app = appList.Apps[0];
            Assert.True(app.Archived);
            Assert.IsType<NotFoundResult>(await crowdfund.ViewCrowdfund(app.Id));
            // Unarchive
            redirect = Assert.IsType<RedirectResult>(apps.ToggleArchive(app.Id).Result);
            Assert.EndsWith("/settings/crowdfund", redirect.Url);
            appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            app = appList.Apps[0];
            Assert.False(app.Archived);
            var crowdfundViewModel = await crowdfund.UpdateCrowdfund(app.Id).AssertViewModelAsync<UpdateCrowdfundViewModel>();
            crowdfundViewModel.Enabled = true;
            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel).Result);
            Assert.IsType<ViewResult>(await crowdfund.ViewCrowdfund(app.Id));
            // Delete
            Assert.IsType<NotFoundResult>(apps2.DeleteApp(app.Id));
            Assert.IsType<ViewResult>(apps.DeleteApp(app.Id));
            var redirectToAction = Assert.IsType<RedirectToActionResult>(apps.DeleteAppPost(app.Id).Result);
            Assert.Equal(nameof(UIStoresController.Dashboard), redirectToAction.ActionName);
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
            var appType = CrowdfundAppType.AppType;
            vm.AppName = "test";
            vm.SelectedAppType = appType;
            var redirect = Assert.IsType<RedirectResult>(apps.CreateApp(user.StoreId, vm).Result);
            Assert.EndsWith("/settings/crowdfund", redirect.Url);
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

            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel).Result);

            var anonAppPubsController = tester.PayTester.GetController<UICrowdfundController>();
            var crowdfundController = user.GetController<UICrowdfundController>();

            Assert.IsType<NotFoundObjectResult>(await anonAppPubsController.ContributeToCrowdfund(app.Id, new ContributeToCrowdfund()
            {
                Amount = new decimal(0.01)
            }, default));

            Assert.IsType<NotFoundResult>(await anonAppPubsController.ViewCrowdfund(app.Id));

            //Scenario 2: Not Enabled But Admin - Allowed
            Assert.IsType<OkObjectResult>(await crowdfundController.ContributeToCrowdfund(app.Id, new ContributeToCrowdfund()
            {
                RedirectToCheckout = false,
                Amount = new decimal(0.01)
            }, default));
            Assert.IsType<ViewResult>(await crowdfundController.ViewCrowdfund(app.Id));
            Assert.IsType<NotFoundResult>(await anonAppPubsController.ViewCrowdfund(app.Id));

            //Scenario 3: Enabled But Start Date > Now - Not Allowed
            crowdfundViewModel.StartDate = DateTime.Today.AddDays(2);
            crowdfundViewModel.Enabled = true;

            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel).Result);
            Assert.IsType<NotFoundObjectResult>(await anonAppPubsController.ContributeToCrowdfund(app.Id, new ContributeToCrowdfund()
            {
                Amount = new decimal(0.01)
            }, default));

            //Scenario 4: Enabled But End Date < Now - Not Allowed
            crowdfundViewModel.StartDate = DateTime.Today.AddDays(-2);
            crowdfundViewModel.EndDate = DateTime.Today.AddDays(-1);
            crowdfundViewModel.Enabled = true;

            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel).Result);
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
            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel).Result);
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
            var appType = CrowdfundAppType.AppType;
            vm.AppName = "test";
            vm.SelectedAppType = appType;
            Assert.IsType<RedirectResult>(apps.CreateApp(user.StoreId, vm).Result);
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
            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel).Result);

            var publicApps = user.GetController<UICrowdfundController>();

            var model = Assert.IsType<ViewCrowdfundViewModel>(Assert
                .IsType<ViewResult>(publicApps.ViewCrowdfund(app.Id).Result).Model);

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
                .IsType<ViewResult>(publicApps.ViewCrowdfund(app.Id).Result).Model);

            Assert.Equal(0m, model.Info.CurrentAmount);
            Assert.Equal(1m, model.Info.CurrentPendingAmount);
            Assert.Equal(0m, model.Info.ProgressPercentage);
            Assert.Equal(1m, model.Info.PendingProgressPercentage);

            TestLogs.LogInformation("Let's check current amount change once payment is confirmed");
            var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, tester.ExplorerNode.Network);
            await tester.ExplorerNode.SendToAddressAsync(invoiceAddress, invoice.BtcDue);
            await tester.ExplorerNode.GenerateAsync(1); // By default invoice confirmed at 1 block
            TestUtils.Eventually(() =>
            {
                model = Assert.IsType<ViewCrowdfundViewModel>(Assert
                .IsType<ViewResult>(publicApps.ViewCrowdfund(app.Id).Result).Model);
                Assert.Equal(1m, model.Info.CurrentAmount);
                Assert.Equal(0m, model.Info.CurrentPendingAmount);
            });

            TestLogs.LogInformation("Because UseAllStoreInvoices is true, let's make sure the invoice is tagged");
            var invoiceEntity = await tester.PayTester.InvoiceRepository.GetInvoice(invoice.Id);
            Assert.True(invoiceEntity.Version >= InvoiceEntity.InternalTagSupport_Version);
            Assert.Contains(AppService.GetAppInternalTag(app.Id), invoiceEntity.InternalTags);

            crowdfundViewModel.UseAllStoreInvoices = false;
            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel).Result);

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
            invoiceEntity = await tester.PayTester.InvoiceRepository.GetInvoice(invoice.Id);
            Assert.DoesNotContain(AppService.GetAppInternalTag(app.Id), invoiceEntity.InternalTags);

            TestLogs.LogInformation("After turning setting a softcap, let's check that only actual payments are counted");
            crowdfundViewModel.EnforceTargetAmount = false;
            crowdfundViewModel.UseAllStoreInvoices = true;
            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel).Result);
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
                .IsType<ViewResult>(publicApps.ViewCrowdfund(app.Id).Result).Model);
                Assert.Equal(0.7m, model.Info.CurrentPendingAmount);
            });
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CrowdfundWithFormNoPerk()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");
            await user.SetNetworkFeeMode(NetworkFeeMode.Never);

            var frmService = tester.PayTester.GetService<FormDataService>();
            var appService = tester.PayTester.GetService<AppService>();
            var crowdfund = user.GetController<UICrowdfundController>();
            var apps = user.GetController<UIAppsController>();
            var appData = new AppData { StoreDataId = user.StoreId, Name = "test", AppType = CrowdfundAppType.AppType };
            await appService.UpdateOrCreateApp(appData);
            var appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            var app = appList.Apps[0];
            apps.HttpContext.SetAppData(appData);
            crowdfund.HttpContext.SetAppData(appData);

            var form = new Form
            {
                Fields =
                [
                    Field.Create("Enter your email", "item1", "test@toto.com", true, null, "email"),
                    Field.Create("Name", "item2", 2.ToString(), true, null),
                    Field.Create("Item3", "invoice_item3", 3.ToString(), true, null)
                ]
            };
            var frmData = new FormData
            {
                StoreId = user.StoreId,
                Name = "frmTest",
                Config = form.ToString()
            };
            await frmService.AddOrUpdateForm(frmData);

            var lstForms = await frmService.GetForms(user.StoreId);
            Assert.NotEmpty(lstForms);

            var crowdfundViewModel = await crowdfund.UpdateCrowdfund(app.Id).AssertViewModelAsync<UpdateCrowdfundViewModel>();
            crowdfundViewModel.FormId = lstForms[0].Id;
            crowdfundViewModel.TargetCurrency = "BTC";
            crowdfundViewModel.Enabled = true;
            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel).Result);

            var vm2 = await crowdfund.CrowdfundForm(app.Id, (decimal?)0.01).AssertViewModelAsync<FormViewModel>();
            var res = await crowdfund.CrowdfundFormSubmit(app.Id, (decimal)0.01, "", vm2);
            Assert.IsNotType<NotFoundObjectResult>(res);
            Assert.IsNotType<BadRequest>(res);
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CrowdfundWithFormAndPerk()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");
            await user.SetNetworkFeeMode(NetworkFeeMode.Never);

            var frmService = tester.PayTester.GetService<FormDataService>();
            var appService = tester.PayTester.GetService<AppService>();
            var crowdfund = user.GetController<UICrowdfundController>();
            var apps = user.GetController<UIAppsController>();
            var appData = new AppData { StoreDataId = user.StoreId, Name = "test", AppType = CrowdfundAppType.AppType };
            await appService.UpdateOrCreateApp(appData);
            var appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            var app = appList.Apps[0];
            apps.HttpContext.SetAppData(appData);
            crowdfund.HttpContext.SetAppData(appData);

            var form = new Form
            {
                Fields =
                [
                    Field.Create("Enter your email", "item1", "test@toto.com", true, null, "email"),
                    Field.Create("Name", "item2", 2.ToString(), true, null),
                    Field.Create("Item3", "invoice_item3", 3.ToString(), true, null)
                ]
            };
            var frmData = new FormData
            {
                StoreId = user.StoreId,
                Name = "frmTest",
                Config = form.ToString()
            };
            await frmService.AddOrUpdateForm(frmData);

            var lstForms = await frmService.GetForms(user.StoreId);
            Assert.NotEmpty(lstForms);

            var crowdfundViewModel = await crowdfund.UpdateCrowdfund(app.Id).AssertViewModelAsync<UpdateCrowdfundViewModel>();
            crowdfundViewModel.FormId = lstForms[0].Id;
            crowdfundViewModel.TargetCurrency = "BTC";
            crowdfundViewModel.Enabled = true;
            crowdfundViewModel.PerksTemplate = "[{\"id\": \"xxx\",\"title\": \"Perk 1\",\"priceType\": \"Fixed\",\"price\": \"0.001\",\"image\": \"\",\"description\": \"\",\"categories\": [],\"disabled\": false}]";
            Assert.IsType<RedirectToActionResult>(crowdfund.UpdateCrowdfund(app.Id, crowdfundViewModel).Result);

            var vm2 = await crowdfund.CrowdfundForm(app.Id, (decimal?)0.01, "xxx").AssertViewModelAsync<FormViewModel>();
            var res = await crowdfund.CrowdfundFormSubmit(app.Id, (decimal)0.01, "xxx", vm2);
            Assert.IsNotType<NotFoundObjectResult>(res);
            Assert.IsNotType<BadRequest>(res);
        }
    }
}
