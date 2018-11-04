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
using BTCPayServer.Tests.Logging;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.CLightning;

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
            await CreateStoreAsync();
            var store = this.GetController<StoresController>();
            var pairingCode = BitPay.RequestClientAuthorization("test", Facade.Merchant);
            Assert.IsType<ViewResult>(await store.RequestPairing(pairingCode.ToString()));
            await store.Pair(pairingCode.ToString(), StoreId);
        }
        public void CreateStore()
        {
            CreateStoreAsync().GetAwaiter().GetResult();
        }

        public T GetController<T>(bool setImplicitStore = true) where T : Controller
        {
            return parent.PayTester.GetController<T>(UserId, setImplicitStore ? StoreId : null);
        }

        public async Task CreateStoreAsync()
        {
            var store = this.GetController<UserStoresController>();
            await store.CreateStore(new CreateStoreViewModel() { Name = "Test Store" });
            StoreId = store.CreatedStoreId;
            parent.Stores.Add(StoreId);
        }

        public BTCPayNetwork SupportedNetwork { get; set; }

        public WalletId RegisterDerivationScheme(string crytoCode, bool segwit = false)
        {
            return RegisterDerivationSchemeAsync(crytoCode, segwit).GetAwaiter().GetResult();
        }
        public async Task<WalletId> RegisterDerivationSchemeAsync(string cryptoCode, bool segwit = false)
        {
            SupportedNetwork = parent.NetworkProvider.GetNetwork(cryptoCode);
            var store = parent.PayTester.GetController<StoresController>(UserId, StoreId);
            ExtKey = new ExtKey().GetWif(SupportedNetwork.NBitcoinNetwork);
            DerivationScheme = new DerivationStrategyFactory(SupportedNetwork.NBitcoinNetwork).Parse(ExtKey.Neuter().ToString() + (segwit ? "" : "-[legacy]"));
            var vm = (StoreViewModel)((ViewResult)store.UpdateStore()).Model;
            vm.SpeedPolicy = SpeedPolicy.MediumSpeed;
            await store.UpdateStore(vm);

            await store.AddDerivationScheme(StoreId, new DerivationSchemeViewModel()
            {
                DerivationScheme = DerivationScheme.ToString(),
                Confirmation = true
            }, cryptoCode);

            return new WalletId(StoreId, cryptoCode);
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
            var storeController = this.GetController<StoresController>();

            string connectionString = null;
            if (connectionType == LightningConnectionType.Charge)
                connectionString = "type=charge;server=" + parent.MerchantCharge.Client.Uri.AbsoluteUri;
            else if (connectionType == LightningConnectionType.CLightning)
                connectionString = "type=clightning;server=" + ((CLightningClient)parent.MerchantLightningD).Address.AbsoluteUri;
            else if (connectionType == LightningConnectionType.LndREST)
                connectionString = $"type=lnd-rest;server={parent.MerchantLnd.Swagger.BaseUrl};allowinsecure=true";
            else
                throw new NotSupportedException(connectionType.ToString());

            await storeController.AddLightningNode(StoreId, new LightningNodeViewModel()
            {
                ConnectionString = connectionString,
                SkipPortTest = true
            }, "save", "BTC");
            if (storeController.ModelState.ErrorCount != 0)
                Assert.False(true, storeController.ModelState.FirstOrDefault().Value.Errors[0].ErrorMessage);
        }
    }
}
