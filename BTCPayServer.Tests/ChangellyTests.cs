using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Payments.Changelly.Models;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class ChangellyTests
    {
        public ChangellyTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanSetChangellyPaymentMethod()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                var controller = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);


                var storeBlob = controller.StoreData.GetStoreBlob();
                Assert.Null(storeBlob.ChangellySettings);

                var updateModel = new UpdateChangellySettingsViewModel()
                {
                    ApiSecret = "secret",
                    ApiKey = "key",
                    ApiUrl = "http://gozo.com",
                    ChangellyMerchantId = "aaa",
                };

                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await controller.UpdateChangellySettings(user.StoreId, updateModel, "save")).ActionName);

                var store = await tester.PayTester.StoreRepository.FindStore(user.StoreId);
                storeBlob = controller.StoreData.GetStoreBlob();
                Assert.NotNull(storeBlob.ChangellySettings);
                Assert.NotNull(storeBlob.ChangellySettings);
                Assert.IsType<ChangellySettings>(storeBlob.ChangellySettings);
                Assert.Equal(storeBlob.ChangellySettings.ApiKey, updateModel.ApiKey);
                Assert.Equal(storeBlob.ChangellySettings.ApiSecret,
                    updateModel.ApiSecret);
                Assert.Equal(storeBlob.ChangellySettings.ApiUrl, updateModel.ApiUrl);
                Assert.Equal(storeBlob.ChangellySettings.ChangellyMerchantId,
                    updateModel.ChangellyMerchantId);
            }
        }


        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanToggleChangellyPaymentMethod()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                var controller = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);

                var updateModel = new UpdateChangellySettingsViewModel()
                {
                    ApiSecret = "secret",
                    ApiKey = "key",
                    ApiUrl = "http://gozo.com",
                    ChangellyMerchantId = "aaa",
                    Enabled = true
                };
                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await controller.UpdateChangellySettings(user.StoreId, updateModel, "save")).ActionName);


                var store = await tester.PayTester.StoreRepository.FindStore(user.StoreId);

                Assert.True(store.GetStoreBlob().ChangellySettings.Enabled);

                updateModel.Enabled = false;

                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await controller.UpdateChangellySettings(user.StoreId, updateModel, "save")).ActionName);

                store = await tester.PayTester.StoreRepository.FindStore(user.StoreId);

                Assert.False(store.GetStoreBlob().ChangellySettings.Enabled);
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async void CannotUseChangellyApiWithoutChangellyPaymentMethodSet()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                var changellyController =
                    tester.PayTester.GetController<ChangellyController>(user.UserId, user.StoreId);
                changellyController.IsTest = true;

                //test non existing payment method
                Assert.IsType<BitpayErrorModel>(Assert
                    .IsType<BadRequestObjectResult>(await changellyController.GetCurrencyList(user.StoreId))
                    .Value);

                var updateModel = CreateDefaultChangellyParams(false);
                var storesController = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);
                //set payment method but disabled


                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await storesController.UpdateChangellySettings(user.StoreId, updateModel, "save")).ActionName);


                Assert.IsType<BitpayErrorModel>(Assert
                    .IsType<BadRequestObjectResult>(await changellyController.GetCurrencyList(user.StoreId))
                    .Value);

                updateModel.Enabled = true;
                //test with enabled method

                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await storesController.UpdateChangellySettings(user.StoreId, updateModel, "save")).ActionName);


                Assert.IsNotType<BitpayErrorModel>(Assert
                    .IsType<OkObjectResult>(await changellyController.GetCurrencyList(user.StoreId))
                    .Value);
            }
        }

        UpdateChangellySettingsViewModel CreateDefaultChangellyParams(bool enabled)
        {
            return new UpdateChangellySettingsViewModel()
            {
                ApiKey = "6ed02cdf1b614d89a8c0ceb170eebb61",
                ApiSecret = "8fbd66a2af5fd15a6b5f8ed0159c5842e32a18538521ffa145bd6c9e124d3483",
                ChangellyMerchantId = "804298eb5753",
                Enabled = enabled
            };
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanGetCurrencyListFromChangelly()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();

                //save changelly settings
                var updateModel = CreateDefaultChangellyParams(true);
                var storesController = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);

                //confirm saved
                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await storesController.UpdateChangellySettings(user.StoreId, updateModel, "save")).ActionName);

                var factory = UnitTest1.CreateBTCPayRateFactory();
                var fetcher = new RateFetcher(factory);
                var httpClientFactory = new MockHttpClientFactory();
                var changellyController = new ChangellyController(
                    new ChangellyClientProvider(tester.PayTester.StoreRepository, httpClientFactory),
                    tester.NetworkProvider, fetcher);
                changellyController.IsTest = true;
                var result = Assert
                    .IsType<OkObjectResult>(await changellyController.GetCurrencyList(user.StoreId))
                    .Value as IEnumerable<CurrencyFull>;
                Assert.True(result.Any());
            }
        }


        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanCalculateToAmountForChangelly()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();

                var updateModel = CreateDefaultChangellyParams(true);
                var storesController = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);

                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await storesController.UpdateChangellySettings(user.StoreId, updateModel, "save")).ActionName);

                var factory = UnitTest1.CreateBTCPayRateFactory();
                var fetcher = new RateFetcher(factory);
                var httpClientFactory = new MockHttpClientFactory();
                var changellyController = new ChangellyController(
                    new ChangellyClientProvider(tester.PayTester.StoreRepository, httpClientFactory),
                    tester.NetworkProvider, fetcher);
                changellyController.IsTest = true;
                Assert.IsType<decimal>(Assert
                    .IsType<OkObjectResult>(await changellyController.CalculateAmount(user.StoreId, "ltc", "btc", 1.0m))
                    .Value);
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanComputeBaseAmount()
        {
            Assert.Equal(1, ChangellyCalculationHelper.ComputeBaseAmount(1, 1));
            Assert.Equal(0.5m, ChangellyCalculationHelper.ComputeBaseAmount(1, 0.5m));
            Assert.Equal(2, ChangellyCalculationHelper.ComputeBaseAmount(0.5m, 1));
            Assert.Equal(4m, ChangellyCalculationHelper.ComputeBaseAmount(1, 4));
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public void CanComputeCorrectAmount()
        {
            Assert.Equal(1, ChangellyCalculationHelper.ComputeCorrectAmount(0.5m, 1, 2));
            Assert.Equal(0.25m, ChangellyCalculationHelper.ComputeCorrectAmount(0.5m, 1, 0.5m));
            Assert.Equal(20, ChangellyCalculationHelper.ComputeCorrectAmount(10, 1, 2));
        }
    }

    public class MockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}
