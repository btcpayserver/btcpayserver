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
        public StoresController CreateStore(string cryptoCode = null)
        {
            return CreateStoreAsync(cryptoCode).GetAwaiter().GetResult();
        }

        public string CryptoCode { get; set; } = "BTC";
        public async Task<StoresController> CreateStoreAsync(string cryptoCode = null)
        {
            cryptoCode = cryptoCode ?? CryptoCode;
            SupportedNetwork = parent.NetworkProvider.GetNetwork(cryptoCode);
            ExtKey = new ExtKey().GetWif(SupportedNetwork.NBitcoinNetwork);
            var store = parent.PayTester.GetController<StoresController>(UserId);
            await store.CreateStore(new CreateStoreViewModel() { Name = "Test Store" });
            StoreId = store.CreatedStoreId;
            DerivationScheme = new DerivationStrategyFactory(SupportedNetwork.NBitcoinNetwork).Parse(ExtKey.Neuter().ToString() + "-[legacy]");
            await store.UpdateStore(StoreId, new StoreViewModel()
            {
                SpeedPolicy = SpeedPolicy.MediumSpeed
            });

            await store.AddDerivationScheme(StoreId, new DerivationSchemeViewModel()
            {
                CryptoCurrency = cryptoCode,
                DerivationSchemeFormat = "BTCPay",
                DerivationScheme = DerivationScheme.ToString(),
            }, "Save");
            return store;
        }

        public BTCPayNetwork SupportedNetwork { get; set; }

        public void RegisterDerivationScheme(string crytoCode)
        {
            RegisterDerivationSchemeAsync(crytoCode).GetAwaiter().GetResult();
        }
        public async Task RegisterDerivationSchemeAsync(string crytoCode)
        {
            var store = parent.PayTester.GetController<StoresController>(UserId);
            var networkProvider = parent.PayTester.GetService<BTCPayNetworkProvider>();
            var derivation = new DerivationStrategyFactory(networkProvider.GetNetwork(crytoCode).NBitcoinNetwork).Parse(ExtKey.Neuter().ToString() + "-[legacy]");
            await store.AddDerivationScheme(StoreId, new DerivationSchemeViewModel()
            {
                CryptoCurrency = crytoCode,
                DerivationSchemeFormat = crytoCode,
                DerivationScheme = derivation.ToString(),
            }, "Save");
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
