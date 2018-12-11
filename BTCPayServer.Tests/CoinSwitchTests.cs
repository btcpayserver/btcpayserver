using BTCPayServer.Controllers;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments.CoinSwitch;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using Xunit.Abstractions;

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
        public async void CanSetCoinSwitchPaymentMethod()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                var controller = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);


                var storeBlob = controller.StoreData.GetStoreBlob();
                Assert.Null(storeBlob.CoinSwitchSettings);

                var updateModel = new UpdateCoinSwitchSettingsViewModel()
                {
                    MerchantId = "aaa",
                };

                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await controller.UpdateCoinSwitchSettings(user.StoreId, updateModel, "save")).ActionName);

                var store = await tester.PayTester.StoreRepository.FindStore(user.StoreId);
                storeBlob = controller.StoreData.GetStoreBlob();
                Assert.NotNull(storeBlob.CoinSwitchSettings);
                Assert.NotNull(storeBlob.CoinSwitchSettings);
                Assert.IsType<CoinSwitchSettings>(storeBlob.CoinSwitchSettings);
                Assert.Equal(storeBlob.CoinSwitchSettings.MerchantId,
                    updateModel.MerchantId);
            }
        }


        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanToggleCoinSwitchPaymentMethod()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
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
