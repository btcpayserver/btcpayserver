using BTCPayServer.Controllers;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments.AutoTrade;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class AutoTradeTests
    {
        public AutoTradeTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanSetAndToggleAutoTradeExchangeSettings()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                foreach (var name in AutoTradeExchangeClientProvider.GetAllSupportedExchangeNames())
                {
                    var user = tester.NewAccount();
                    user.GrantAccess();
                    var controller = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);
                    var updateModel = new UpdateAutoTradeSettingsViewModel()
                    {
                        ApiKey = "apiKey",
                        ApiSecret = "apiSecret",
                        ApiUrl = "http://gozo.com",
                        Enabled = true
                    };

                    Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                        await controller.UpdateAutoTradeSettings(user.StoreId, updateModel, name, "save")).ActionName);

                    var store = await tester.PayTester.StoreRepository.FindStore(user.StoreId);
                    Assert.True(store.GetStoreBlob().AutoTradeExchangeSettings.Enabled);
                    updateModel.Enabled = false;
                    Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                        await controller.UpdateAutoTradeSettings(user.StoreId, updateModel, name, "save")).ActionName);

                    store = await tester.PayTester.StoreRepository.FindStore(user.StoreId);
                    Assert.False(store.GetStoreBlob().AutoTradeExchangeSettings.Enabled);
                }
            }
        }
        [Fact]
        [Trait("Integration", "Integration")]
        public async void CannotUseAutoExchangeFeatureWithoutMethodSet()
        {
        }
    }
}
