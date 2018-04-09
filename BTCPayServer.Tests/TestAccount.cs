using BTCPayServer.Controllers;
using System.Linq;
using BTCPayServer.Models.AccountViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using NBXplorer.DerivationStrategy;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;

namespace BTCPayServer.Tests
{
    public class TestAccount
    {
        ServerTester parent;
        public TestAccount(ServerTester parent)
        {
            this.parent = parent;
            BitPay = new Bitpay(new Key(), parent.PayTester.ServerUri);
        }

        public void GrantAccess()
        {
            GrantAccessAsync().GetAwaiter().GetResult();
        }

        public void Register()
        {
            RegisterAsync().GetAwaiter().GetResult();
        }

        public BitcoinExtKey ExtKey
        {
            get; set;
        }

        public async Task GrantAccessAsync()
        {
            await RegisterAsync();
            var store = await CreateStoreAsync();
            var pairingCode = BitPay.RequestClientAuthorization("test", Facade.Merchant);
            Assert.IsType<ViewResult>(await store.RequestPairing(pairingCode.ToString()));
            await store.Pair(pairingCode.ToString(), StoreId);
        }
        public StoresController CreateStore()
        {
            return CreateStoreAsync().GetAwaiter().GetResult();
        }

        public T GetController<T>() where T : Controller
        {
            return parent.PayTester.GetController<T>(UserId);
        }

        public async Task<StoresController> CreateStoreAsync()
        {
            var store = parent.PayTester.GetController<UserStoresController>(UserId);
            await store.CreateStore(new CreateStoreViewModel() { Name = "Test Store" });
            StoreId = store.CreatedStoreId;
            var store2 = parent.PayTester.GetController<StoresController>(UserId);
            store2.CreatedStoreId = store.CreatedStoreId;
            return store2;
        }

        public BTCPayNetwork SupportedNetwork { get; set; }

        public void RegisterDerivationScheme(string crytoCode)
        {
            RegisterDerivationSchemeAsync(crytoCode).GetAwaiter().GetResult();
        }
        public async Task RegisterDerivationSchemeAsync(string cryptoCode)
        {
            SupportedNetwork = parent.NetworkProvider.GetNetwork(cryptoCode);
            var store = parent.PayTester.GetController<StoresController>(UserId);
            ExtKey = new ExtKey().GetWif(SupportedNetwork.NBitcoinNetwork);
            DerivationScheme = new DerivationStrategyFactory(SupportedNetwork.NBitcoinNetwork).Parse(ExtKey.Neuter().ToString() + "-[legacy]");
            var vm = (StoreViewModel)((ViewResult)await store.UpdateStore(StoreId)).Model;
            vm.SpeedPolicy = SpeedPolicy.MediumSpeed;
            await store.UpdateStore(StoreId, vm);

            await store.AddDerivationScheme(StoreId, new DerivationSchemeViewModel()
            {
                DerivationScheme = DerivationScheme.ToString(),
                Confirmation = true
            }, cryptoCode);
        }

        public DerivationStrategyBase DerivationScheme { get; set; }

        private async Task RegisterAsync()
        {
            var account = parent.PayTester.GetController<AccountController>();
            await account.Register(new RegisterViewModel()
            {
                Email = Guid.NewGuid() + "@toto.com",
                ConfirmPassword = "Kitten0@",
                Password = "Kitten0@",
            });
            UserId = account.RegisteredUserId;
        }

        public Bitpay BitPay
        {
            get; set;
        }
        public string UserId
        {
            get; set;
        }

        public string StoreId
        {
            get; set;
        }
        
        public void RegisterLightningNode(string cryptoCode, LightningConnectionType connectionType)
        {
            RegisterLightningNodeAsync(cryptoCode, connectionType).GetAwaiter().GetResult();
        }

        public async Task RegisterLightningNodeAsync(string cryptoCode, LightningConnectionType connectionType)
        {
            var storeController = parent.PayTester.GetController<StoresController>(UserId);
            await storeController.AddLightningNode(StoreId, new LightningNodeViewModel()
            {
                Url = connectionType == LightningConnectionType.Charge ? parent.MerchantCharge.Client.Uri.AbsoluteUri :
                      connectionType == LightningConnectionType.CLightning ? parent.MerchantLightningD.Address.AbsoluteUri
                      : throw new NotSupportedException(connectionType.ToString()),
                SkipPortTest = true
            }, "save", "BTC");
            if (storeController.ModelState.ErrorCount != 0)
                Assert.False(true, storeController.ModelState.FirstOrDefault().Value.Errors[0].ErrorMessage);
        }
}
}
