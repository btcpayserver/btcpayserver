using BTCPayServer.Controllers;
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
        public async Task<StoresController> CreateStoreAsync()
        {
            ExtKey = new ExtKey().GetWif(parent.Network);
            var store = parent.PayTester.GetController<StoresController>(UserId);
            await store.CreateStore(new CreateStoreViewModel() { Name = "Test Store" });
            StoreId = store.CreatedStoreId;
            DerivationScheme = new DerivationStrategyFactory(parent.Network).Parse(ExtKey.Neuter().ToString() + "-[legacy]");
            await store.UpdateStore(StoreId, new StoreViewModel()
            {
                DerivationScheme = DerivationScheme.ToString(),
                SpeedPolicy = SpeedPolicy.MediumSpeed
            }, "Save");
            return store;
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
    }
}
