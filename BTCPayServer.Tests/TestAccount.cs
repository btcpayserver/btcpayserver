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
using BTCPayServer.Data;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using Microsoft.AspNetCore.Identity;

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

        public async Task MakeAdmin()
        {
            var userManager = parent.PayTester.GetService<UserManager<ApplicationUser>>();
            var u = await userManager.FindByIdAsync(UserId);
            await userManager.AddToRoleAsync(u, Roles.ServerAdmin);
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

        public void SetNetworkFeeMode(NetworkFeeMode mode)
        {
            ModifyStore((store) =>
            {
                store.NetworkFeeMode = mode;
            });
        }
        public void ModifyStore(Action<StoreViewModel> modify)
        {
            var storeController = GetController<StoresController>();
            StoreViewModel store = (StoreViewModel)((ViewResult)storeController.UpdateStore()).Model;
            modify(store);
            storeController.UpdateStore(store).GetAwaiter().GetResult();
        }

        public T GetController<T>(bool setImplicitStore = true) where T : Controller
        {
            var controller = parent.PayTester.GetController<T>(UserId, setImplicitStore ? StoreId : null, IsAdmin);
            return controller;
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
            SupportedNetwork = parent.NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            var store = parent.PayTester.GetController<StoresController>(UserId, StoreId);
            ExtKey = new ExtKey().GetWif(SupportedNetwork.NBitcoinNetwork);
            DerivationScheme = SupportedNetwork.NBXplorerNetwork.DerivationStrategyFactory.Parse(ExtKey.Neuter().ToString() + (segwit ? "" : "-[legacy]"));
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
            RegisterDetails = new RegisterViewModel()
            {
                Email = Guid.NewGuid() + "@toto.com",
                ConfirmPassword = "Kitten0@",
                Password = "Kitten0@",
            };
            await account.Register(RegisterDetails);
            UserId = account.RegisteredUserId;
        }

        public RegisterViewModel RegisterDetails{ get; set; }

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
        public bool IsAdmin { get; internal set; }

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

        public async Task<BTCPayOpenIdClient> RegisterOpenIdClient(OpenIddictApplicationDescriptor descriptor, string secret = null)
        {
          var openIddictApplicationManager = parent.PayTester.GetService<OpenIddictApplicationManager<BTCPayOpenIdClient>>();
          var client = new BTCPayOpenIdClient { Id = Guid.NewGuid().ToString(), ApplicationUserId = UserId};
          await openIddictApplicationManager.PopulateAsync(client, descriptor);
          await openIddictApplicationManager.CreateAsync(client, secret);
          return client;
        }
    }
}
