using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Models.AccountViewModels;
using BTCPayServer.Tests.Logging;
using BTCPayServer.U2F;
using BTCPayServer.U2F.Models;
using Microsoft.AspNetCore.Mvc;
using U2F.Core.Models;
using U2F.Core.Utils;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class U2FTests
    {
        public const int TestTimeout = 60_000;

        public U2FTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }


        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task U2ftest()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                var accountController = tester.PayTester.GetController<AccountController>();
                var manageController = user.GetController<ManageController>();
                var mock = new MockU2FService(tester.PayTester.GetService<ApplicationDbContextFactory>());
                manageController._u2FService = mock;
                accountController._u2FService = mock;

                Assert
                    .IsType<RedirectToActionResult>(await accountController.Login(new LoginViewModel()
                    {
                        Email = user.RegisterDetails.Email, Password = user.RegisterDetails.Password
                    }));

                Assert.Empty(Assert.IsType<U2FAuthenticationViewModel>(Assert
                    .IsType<ViewResult>(await manageController.U2FAuthentication()).Model).Devices);

                var addDeviceVM = Assert.IsType<AddU2FDeviceViewModel>(Assert
                    .IsType<ViewResult>(manageController.AddU2FDevice("testdevice")).Model);

                Assert.NotEmpty(addDeviceVM.Challenge);
                Assert.Equal("testdevice", addDeviceVM.Name);
                Assert.NotEmpty(addDeviceVM.Version);
                Assert.Null(addDeviceVM.DeviceResponse);

                var devReg = new DeviceRegistration(Guid.NewGuid().ToByteArray(), Guid.NewGuid().ToByteArray(),
                    Guid.NewGuid().ToByteArray(), 1);

                mock.GetDevReg = () => devReg;
                mock.StartedAuthentication = () =>
                    new StartedAuthentication("chocolate", addDeviceVM.AppId,
                        devReg.KeyHandle.ByteArrayToBase64String());
                addDeviceVM.DeviceResponse = new RegisterResponse("ss",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes("{typ:'x', challenge: 'fff'}"))).ToJson();
                Assert
                    .IsType<RedirectToActionResult>(await manageController.AddU2FDevice(addDeviceVM));

                Assert.Single(Assert.IsType<U2FAuthenticationViewModel>(Assert
                    .IsType<ViewResult>(await manageController.U2FAuthentication()).Model).Devices);

                var secondaryLoginViewModel = Assert.IsType<SecondaryLoginViewModel>(Assert
                    .IsType<ViewResult>(await accountController.Login(new LoginViewModel()
                    {
                        Email = user.RegisterDetails.Email, Password = user.RegisterDetails.Password
                    })).Model);
                Assert.NotNull(secondaryLoginViewModel.LoginWithU2FViewModel);
                Assert.Single(secondaryLoginViewModel.LoginWithU2FViewModel.Challenges);
                Assert.Equal(secondaryLoginViewModel.LoginWithU2FViewModel.Challenge,
                    secondaryLoginViewModel.LoginWithU2FViewModel.Challenges.First().challenge);

                secondaryLoginViewModel.LoginWithU2FViewModel.DeviceResponse = new AuthenticateResponse(
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(
                        "{typ:'x', challenge: '" + secondaryLoginViewModel.LoginWithU2FViewModel.Challenge + "'}")),
                    "dd", devReg.KeyHandle.ByteArrayToBase64String()).ToJson();
                Assert
                    .IsType<RedirectToActionResult>(
                        await accountController.LoginWithU2F(secondaryLoginViewModel.LoginWithU2FViewModel));
            }
        }

        public class MockU2FService : U2FService
        {
            public Func<DeviceRegistration> GetDevReg;
            public Func<StartedAuthentication> StartedAuthentication;

            public MockU2FService(ApplicationDbContextFactory contextFactory) : base(contextFactory)
            {
            }

            protected override StartedRegistration StartDeviceRegistrationCore(string appId)
            {
                return global::U2F.Core.Crypto.U2F.StartRegistration(appId);
            }

            protected override DeviceRegistration FinishRegistrationCore(StartedRegistration startedRegistration,
                RegisterResponse registerResponse)
            {
                return GetDevReg();
            }

            protected override StartedAuthentication StartAuthenticationCore(string appId, U2FDevice registeredDevice)
            {
                return StartedAuthentication();
            }

            protected override void FinishAuthenticationCore(StartedAuthentication authentication,
                AuthenticateResponse authenticateResponse, DeviceRegistration registration)
            {
            }
        }
    }
}
