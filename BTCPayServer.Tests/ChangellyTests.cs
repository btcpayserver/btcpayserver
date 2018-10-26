using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
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
        public async void CannotUseChangellyApiWithoutChangellyPaymentMethodSet()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                var changellyController =
                    tester.PayTester.GetController<ChangellyController>(user.UserId, user.StoreId);

                //test non existing payment method
                Assert.IsType<BitpayErrorModel>(Assert
                    .IsType<BadRequestObjectResult>(await changellyController.GetCurrencyList(user.StoreId))
                    .Value);

                var updateModel = new UpdateChangellySettingsViewModel
                {
                    Enabled = false
                };
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


        [Fact]
        public async void CanGetCurrencyListFromChangelly()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();

                //save changelly settings
                var updateModel = new UpdateChangellySettingsViewModel()
                {
                    ApiSecret = "secret",
                    ApiKey = "key",
                    ApiUrl = "http://gozo.com",
                    ChangellyMerchantId = "aaa"
                };
                var storesController = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);

                //confirm saved
                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await storesController.UpdateChangellySettings(user.StoreId, updateModel, "save")).ActionName);


                var mockChangelly = new MockChangelly(new MockHttpClientFactory(), updateModel.ApiKey, updateModel.ApiSecret, updateModel.ApiUrl);
                var mock = new MockChangellyClientProvider(mockChangelly, tester.PayTester.StoreRepository);
                
                var factory = UnitTest1.CreateBTCPayRateFactory();
                var fetcher = new RateFetcher(factory);

                var changellyController = new ChangellyController(mock, tester.NetworkProvider, fetcher);

                
                mockChangelly.GetCurrenciesFullResult = new List<CurrencyFull>()
                {
                    new CurrencyFull()
                    {
                        Name = "a",
                        Enable = true,
                        PayInConfirmations = 10,
                        FullName = "aa",
                        ImageLink = ""
                    }
                };
                var result = Assert
                    .IsType<OkObjectResult>(await changellyController.GetCurrencyList(user.StoreId))
                    .Value as IEnumerable<CurrencyFull>;
                Assert.Equal(1, mockChangelly.GetCurrenciesFullCallCount);

            }
        }


        [Fact]
        public async void CanCalculateToAmountForChangelly()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();

                var updateModel = new UpdateChangellySettingsViewModel()
                {
                    ApiSecret = "secret",
                    ApiKey = "key",
                    ApiUrl = "http://gozo.com",
                    ChangellyMerchantId = "aaa"
                };
                var storesController = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);

                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await storesController.UpdateChangellySettings(user.StoreId, updateModel, "save")).ActionName);

                var mockChangelly = new MockChangelly(new MockHttpClientFactory(),  updateModel.ApiKey, updateModel.ApiSecret, updateModel.ApiUrl);
                var mock = new MockChangellyClientProvider(mockChangelly, tester.PayTester.StoreRepository);
                
                var factory = UnitTest1.CreateBTCPayRateFactory();
                var fetcher = new RateFetcher(factory);
                
                var changellyController = new ChangellyController(mock,tester.NetworkProvider,fetcher);

                mockChangelly.GetExchangeAmountResult = (from, to, amount) =>
                {
                    Assert.Equal("A", from);
                    Assert.Equal("B", to);

                    switch (mockChangelly.GetExchangeAmountCallCount)
                    {
                        case 1:
                            return 0.5m;
                        default:
                            return 1.01m;
                    }
                };

                Assert.IsType<decimal>(Assert
                    .IsType<OkObjectResult>(await changellyController.CalculateAmount(user.StoreId, "A", "B", 1.0m)).Value);
                Assert.True(mockChangelly.GetExchangeAmountCallCount > 1);
            }
        }
    }

    public class MockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return  new HttpClient();
        }
    }
    
    public class MockChangelly : Changelly
    {
        public IEnumerable<CurrencyFull> GetCurrenciesFullResult { get; set; }

        public delegate decimal ParamsFunc<T1, T2, T3, TResult>(T1 arg1, T2 arg2, T3 arg3);

        public ParamsFunc<string, string, decimal, (decimal amount, bool Success, string Error)> GetExchangeAmountResult
        {
            get;
            set;
        }

        public int GetCurrenciesFullCallCount { get; set; } = 0;
        public int GetExchangeAmountCallCount { get; set; } = 0;

        public MockChangelly(IHttpClientFactory httpClientFactory,  string apiKey, string apiSecret, string apiUrl) : base(httpClientFactory, apiKey, apiSecret, apiUrl)
        {
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public override async Task<IEnumerable<CurrencyFull>> GetCurrenciesFull()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            GetCurrenciesFullCallCount++;
            return GetCurrenciesFullResult;
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public override async Task<decimal> GetExchangeAmount(string fromCurrency,
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
            string toCurrency, decimal amount)
        {
            GetExchangeAmountCallCount++;
            return GetExchangeAmountResult.Invoke(fromCurrency, toCurrency, amount);
        }
    }

    public class MockChangellyClientProvider : ChangellyClientProvider
    {
        public MockChangelly MockChangelly;

        public MockChangellyClientProvider(
            MockChangelly mockChangelly,
            StoreRepository storeRepository) : base(storeRepository, new MockHttpClientFactory())
        {
            MockChangelly = mockChangelly;
        }

        public override bool TryGetChangellyClient(string storeId, out string error, out Changelly changelly)
        {
            error = null;
            changelly = MockChangelly;
            return true;
        }
    }
}
