using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Controllers;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Services.Stores;
using BTCPayServer.Tests.Logging;
using Changelly.ResponseModel;
using Microsoft.AspNetCore.Mvc;
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
                    ApiUrl = "url",
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
                    ApiUrl = "url",
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

                var updateModel = new UpdateChangellySettingsViewModel()
                {
                    ApiSecret = "secret",
                    ApiKey = "key",
                    ApiUrl = "url",
                    ChangellyMerchantId = "aaa",
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
                    .IsType<BadRequestObjectResult>(await changellyController.GetCurrencyList(user.StoreId))
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

                var updateModel = new UpdateChangellySettingsViewModel()
                {
                    ApiSecret = "secret",
                    ApiKey = "key",
                    ApiUrl = "url",
                    ChangellyMerchantId = "aaa"
                };
                var storesController = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);


                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await storesController.UpdateChangellySettings(user.StoreId, updateModel, "save")).ActionName);


                var mock = new MockChangellyClientProvider(tester.PayTester.StoreRepository, tester.NetworkProvider);
                var changellyController = new ChangellyController(mock);

                mock.GetCurrenciesFullResult = (new List<CurrencyFull>()
                {
                    new CurrencyFull()
                    {
                        Name = "a",
                        Enable = true,
                        PayInConfirmations = 10,
                        FullName = "aa",
                        ImageLink = ""
                    }
                }, true, "");
                var result = ((IList<CurrencyFull> currency, bool Success, string Error))Assert
                    .IsType<OkObjectResult>(await changellyController.GetCurrencyList(user.StoreId)).Value;
                Assert.Equal(1, mock.GetCurrenciesFullCallCount);
                Assert.Equal(mock.GetCurrenciesFullResult.currency.Count, result.currency.Count);

                mock.GetCurrenciesFullResult = (new List<CurrencyFull>()
                {
                    new CurrencyFull()
                    {
                        Name = "a",
                        Enable = true,
                        PayInConfirmations = 10,
                        FullName = "aa",
                        ImageLink = ""
                    }
                }, false, "");
                Assert
                    .IsType<BadRequestObjectResult>(await changellyController.GetCurrencyList(user.StoreId));
                Assert.Equal(2, mock.GetCurrenciesFullCallCount);
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
                    ApiUrl = "url",
                    ChangellyMerchantId = "aaa"
                };
                var storesController = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);

                Assert.Equal("UpdateStore", Assert.IsType<RedirectToActionResult>(
                    await storesController.UpdateChangellySettings(user.StoreId, updateModel, "save")).ActionName);

                var mock = new MockChangellyClientProvider(tester.PayTester.StoreRepository, tester.NetworkProvider);
                var changellyController = new ChangellyController(mock);

                mock.GetExchangeAmountResult = (from, to, amount) =>
                {
                    Assert.Equal("A", from);
                    Assert.Equal("B", to);

                    switch (mock.GetExchangeAmountCallCount)
                    {
                        case 1:
                            return (0.5, true, null);
                            break;
                        default:
                            return (1.01, true, null);
                            break;
                    }
                };

                Assert.IsType<double>(Assert
                    .IsType<OkObjectResult>(changellyController.CalculateAmount(user.StoreId, "A", "B", 1.0)).Value);
                Assert.True(mock.GetExchangeAmountCallCount > 1);
            }
        }
    }

    public class MockChangellyClientProvider : ChangellyClientProvider
    {
        public MockChangellyClientProvider(
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider) : base(storeRepository, btcPayNetworkProvider)
        {
        }

        public (IList<CurrencyFull> currency, bool Success, string Error) GetCurrenciesFullResult { get; set; }

        public delegate TResult ParamsFunc<T1, T2, T3, TResult>(T1 arg1, T2 arg2, T3 arg3);

        public ParamsFunc<string, string, double, (double amount, bool Success, string Error)> GetExchangeAmountResult
        {
            get;
            set;
        }

        public int GetCurrenciesFullCallCount { get; set; } = 0;
        public int GetExchangeAmountCallCount { get; set; } = 0;

        public override (IList<CurrencyFull> currency, bool Success, string Error) GetCurrenciesFull(
            Changelly.Changelly client)
        {
            GetCurrenciesFullCallCount++;
            return GetCurrenciesFullResult;
        }

        public override (double amount, bool Success, string Error) GetExchangeAmount(Changelly.Changelly client,
            string fromCurrency, string toCurrency,
            double amount)
        {
            GetExchangeAmountCallCount++;
            return GetExchangeAmountResult.Invoke(fromCurrency, toCurrency, amount);
        }
    }
}
