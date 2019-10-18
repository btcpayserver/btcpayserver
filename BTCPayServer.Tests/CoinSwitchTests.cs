using BTCPayServer.Controllers;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments.CoinSwitch;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using Xunit.Abstractions;
using BTCPayServer.Data;
using System.Threading.Tasks;

namespace BTCPayServer.Tests
{
    public class CoinSwitchTests
    {
        public CoinSwitchTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanSetCoinSwitchPaymentMethod()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                var controller = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);


                var storeBlob = controller.CurrentStore.GetStoreBlob();
                Assert.Null(storeBlob.CoinSwitchSettings);

                var updateModel = new UpdateCoinSwitchSettingsViewModel()
                {
                    MerchantId = "aaa",
                };

                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await controller.UpdateCoinSwitchSettings(user.StoreId, updateModel, "save")).ActionName);

                var store = await tester.PayTester.StoreRepository.FindStore(user.StoreId);
                storeBlob = controller.CurrentStore.GetStoreBlob();
                Assert.NotNull(storeBlob.CoinSwitchSettings);
                Assert.NotNull(storeBlob.CoinSwitchSettings);
                Assert.IsType<CoinSwitchSettings>(storeBlob.CoinSwitchSettings);
                Assert.Equal(storeBlob.CoinSwitchSettings.MerchantId,
                    updateModel.MerchantId);
            }
        }


        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanToggleCoinSwitchPaymentMethod()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                var controller = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);

                var updateModel = new UpdateCoinSwitchSettingsViewModel()
                {
                    MerchantId = "aaa",
                    Enabled = true
                };
                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await controller.UpdateCoinSwitchSettings(user.StoreId, updateModel, "save")).ActionName);


                var store = await tester.PayTester.StoreRepository.FindStore(user.StoreId);

                Assert.True(store.GetStoreBlob().CoinSwitchSettings.Enabled);

                updateModel.Enabled = false;

                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await controller.UpdateCoinSwitchSettings(user.StoreId, updateModel, "save")).ActionName);

                store = await tester.PayTester.StoreRepository.FindStore(user.StoreId);

                Assert.False(store.GetStoreBlob().CoinSwitchSettings.Enabled);
            }
        }
   
    }
}
