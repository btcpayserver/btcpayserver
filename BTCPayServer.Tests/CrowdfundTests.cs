using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Crowdfund;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Hubs;
using BTCPayServer.Models;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Payments.Changelly.Models;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitpayClient;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class CrowdfundTests
    {
        public CrowdfundTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanCreateAndDeleteCrowdfundApp()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                var user2 = tester.NewAccount();
                user2.GrantAccess();
                var apps = user.GetController<AppsController>();
                var apps2 = user2.GetController<AppsController>();
                var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp().Result).Model);
                Assert.NotNull(vm.SelectedAppType);
                Assert.Null(vm.Name);
                vm.Name = "test";
                vm.SelectedAppType = AppType.Crowdfund.ToString();
                var redirectToAction = Assert.IsType<RedirectToActionResult>(apps.CreateApp(vm).Result);
                Assert.Equal(nameof(apps.UpdateCrowdfund), redirectToAction.ActionName);
                var appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps().Result).Model);
                var appList2 =
                    Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps2.ListApps().Result).Model);
                Assert.Single(appList.Apps);
                Assert.Empty(appList2.Apps);
                Assert.Equal("test", appList.Apps[0].AppName);
                Assert.Equal(apps.CreatedAppId, appList.Apps[0].Id);
                Assert.True(appList.Apps[0].IsOwner);
                Assert.Equal(user.StoreId, appList.Apps[0].StoreId);
                Assert.IsType<NotFoundResult>(apps2.DeleteApp(appList.Apps[0].Id).Result);
                Assert.IsType<ViewResult>(apps.DeleteApp(appList.Apps[0].Id).Result);
                redirectToAction = Assert.IsType<RedirectToActionResult>(apps.DeleteAppPost(appList.Apps[0].Id).Result);
                Assert.Equal(nameof(apps.ListApps), redirectToAction.ActionName);
                appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps().Result).Model);
                Assert.Empty(appList.Apps);
            }
        }
        
        
        
        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanContributeOnlyWhenAllowed()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                var apps = user.GetController<AppsController>();
                var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp().Result).Model);
                vm.Name = "test";
                vm.SelectedAppType = AppType.Crowdfund.ToString();
                Assert.IsType<RedirectToActionResult>(apps.CreateApp(vm).Result);
                var appId = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps().Result).Model)
                    .Apps[0].Id;
                
                //Scenario 1: Not Enabled - Not Allowed
                var crowdfundViewModel = Assert.IsType<UpdateCrowdfundViewModel>(Assert
                    .IsType<ViewResult>(apps.UpdateCrowdfund(appId).Result).Model);
                crowdfundViewModel.TargetCurrency = "BTC";
                crowdfundViewModel.Enabled = false;
                crowdfundViewModel.EndDate = null;
                
                Assert.IsType<RedirectToActionResult>(apps.UpdateCrowdfund(appId, crowdfundViewModel).Result);
                
                var anonAppPubsController = tester.PayTester.GetController<AppsPublicController>();
                var publicApps = user.GetController<AppsPublicController>();


                Assert.IsType<NotFoundObjectResult>(await anonAppPubsController.ContributeToCrowdfund(appId, new ContributeToCrowdfund()
                {
                    Amount = new decimal(0.01)
                }));
                
                Assert.IsType<NotFoundResult>(await anonAppPubsController.ViewCrowdfund(appId, string.Empty));
                
                //Scenario 2: Not Enabled But Admin - Allowed
                Assert.IsType<OkObjectResult>(await publicApps.ContributeToCrowdfund(appId, new ContributeToCrowdfund()
                {
                    RedirectToCheckout = false,
                    Amount = new decimal(0.01)
                }));
                Assert.IsType<ViewResult>(await publicApps.ViewCrowdfund(appId, string.Empty));
                Assert.IsType<NotFoundResult>(await anonAppPubsController.ViewCrowdfund(appId, string.Empty));
                
                //Scenario 3: Enabled But Start Date > Now - Not Allowed
                crowdfundViewModel.StartDate= DateTime.Today.AddDays(2);
                crowdfundViewModel.Enabled = true;
                
                Assert.IsType<RedirectToActionResult>(apps.UpdateCrowdfund(appId, crowdfundViewModel).Result);
                Assert.IsType<NotFoundObjectResult>(await anonAppPubsController.ContributeToCrowdfund(appId, new ContributeToCrowdfund()
                {
                    Amount = new decimal(0.01)
                }));
                
                //Scenario 4: Enabled But End Date < Now - Not Allowed
                
                crowdfundViewModel.StartDate= DateTime.Today.AddDays(-2);
                crowdfundViewModel.EndDate= DateTime.Today.AddDays(-1);
                crowdfundViewModel.Enabled = true;
                
                Assert.IsType<RedirectToActionResult>(apps.UpdateCrowdfund(appId, crowdfundViewModel).Result);
                Assert.IsType<NotFoundObjectResult>(await anonAppPubsController.ContributeToCrowdfund(appId, new ContributeToCrowdfund()
                {
                    Amount = new decimal(0.01)
                }));
                
                
                //Scenario 5: Enabled and within correct timeframe, however target is enforced and Amount is Over - Not Allowed
                crowdfundViewModel.StartDate= DateTime.Today.AddDays(-2);
                crowdfundViewModel.EndDate= DateTime.Today.AddDays(2);
                crowdfundViewModel.Enabled = true;
                crowdfundViewModel.TargetAmount = 1;
                crowdfundViewModel.TargetCurrency = "BTC";
                crowdfundViewModel.EnforceTargetAmount = true;
                Assert.IsType<RedirectToActionResult>(apps.UpdateCrowdfund(appId, crowdfundViewModel).Result);
                Assert.IsType<NotFoundObjectResult>(await anonAppPubsController.ContributeToCrowdfund(appId, new ContributeToCrowdfund()
                {
                    Amount = new decimal(1.01)
                }));
                
                //Scenario 6: Allowed
                Assert.IsType<OkObjectResult>(await anonAppPubsController.ContributeToCrowdfund(appId, new ContributeToCrowdfund()
                {
                    Amount = new decimal(0.05)
                }));
                
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanComputeCrowdfundModel()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                var apps = user.GetController<AppsController>();
                var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp().Result).Model);
                vm.Name = "test";
                vm.SelectedAppType = AppType.Crowdfund.ToString();
                Assert.IsType<RedirectToActionResult>(apps.CreateApp(vm).Result);
                var appId = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps().Result).Model)
                    .Apps[0].Id;
                
                var crowdfundViewModel = Assert.IsType<UpdateCrowdfundViewModel>(Assert
                    .IsType<ViewResult>(apps.UpdateCrowdfund(appId).Result).Model);
                crowdfundViewModel.Enabled = true;
                crowdfundViewModel.EndDate = null;
                crowdfundViewModel.TargetAmount = 100;
                crowdfundViewModel.TargetCurrency = "BTC";
                crowdfundViewModel.UseAllStoreInvoices = true;
                Assert.IsType<RedirectToActionResult>(apps.UpdateCrowdfund(appId, crowdfundViewModel).Result);
                
                var anonAppPubsController = tester.PayTester.GetController<AppsPublicController>();
                var publicApps = user.GetController<AppsPublicController>();

                var model = Assert.IsType<ViewCrowdfundViewModel>(Assert
                    .IsType<ViewResult>(publicApps.ViewCrowdfund(appId, String.Empty).Result).Model);
                
                
                Assert.Equal(crowdfundViewModel.TargetAmount, model.TargetAmount );
                Assert.Equal(crowdfundViewModel.EndDate, model.EndDate );
                Assert.Equal(crowdfundViewModel.StartDate, model.StartDate );
                Assert.Equal(crowdfundViewModel.TargetCurrency, model.TargetCurrency );
                Assert.Equal(0m, model.Info.CurrentAmount );
                Assert.Equal(0m, model.Info.CurrentPendingAmount);
                Assert.Equal(0m, model.Info.ProgressPercentage);
                
                
                
                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Buyer = new Buyer() { email = "test@fwf.com" },
                    Price = 1m,
                    Currency = "BTC",
                    PosData = "posData",
                    OrderId = $"{CrowdfundHubStreamer.CrowdfundInvoiceOrderIdPrefix}{appId}",
                    ItemDesc = "Some description",
                    TransactionSpeed = "high",
                    FullNotifications = true
                }, Facade.Merchant);

                
                model = Assert.IsType<ViewCrowdfundViewModel>(Assert
                    .IsType<ViewResult>(publicApps.ViewCrowdfund(appId, String.Empty).Result).Model);
                
                var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, tester.ExplorerNode.Network);
               
                
                
                tester.ExplorerNode.SendToAddress(invoiceAddress,invoice.BtcDue);
                Assert.Equal(0m ,model.Info.CurrentAmount );
                Assert.Equal(1m, model.Info.CurrentPendingAmount);
                Assert.Equal( 0m, model.Info.ProgressPercentage);
                Assert.Equal(1m, model.Info.PendingProgressPercentage);

                
    
               
            }

            
        }
        
    }


}
