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
using Microsoft.AspNetCore.Identity;
using NBXplorer.Models;
using BTCPayServer.Client;

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
            IsAdmin = true;
        }

        public async Task<BTCPayServerClient> CreateClient(params string[] permissions)
        {
            var manageController = parent.PayTester.GetController<ManageController>(UserId, StoreId, IsAdmin);
            var x = Assert.IsType<RedirectToActionResult>(await manageController.AddApiKey(
                new ManageController.AddApiKeyViewModel()
                {
                    PermissionValues = permissions.Select(s => new ManageController.AddApiKeyViewModel.PermissionValueItem()
                    {
                        Permission = s,
                        Value = true
                    }).ToList(),
                    StoreMode = ManageController.AddApiKeyViewModel.ApiKeyStoreMode.AllStores
                }));
            var statusMessage = manageController.TempData.GetStatusMessageModel();
            Assert.NotNull(statusMessage);
            var apiKey = statusMessage.Html.Substring(statusMessage.Html.IndexOf("<code>") + 6);
            apiKey = apiKey.Substring(0, apiKey.IndexOf("</code>"));
            return new BTCPayServerClient(parent.PayTester.ServerUri, apiKey);
        }

        public void Register()
        {
            RegisterAsync().GetAwaiter().GetResult();
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

        public WalletId RegisterDerivationScheme(string crytoCode, bool segwit = false, bool importKeysToNBX = false)
        {
            return RegisterDerivationSchemeAsync(crytoCode, segwit, importKeysToNBX).GetAwaiter().GetResult();
        }
        public async Task<WalletId> RegisterDerivationSchemeAsync(string cryptoCode, bool segwit = false, bool importKeysToNBX = false)
        {
            SupportedNetwork = parent.NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            var store = parent.PayTester.GetController<StoresController>(UserId, StoreId);
            GenerateWalletResponseV = await parent.ExplorerClient.GenerateWalletAsync(new GenerateWalletRequest()
            {
                ScriptPubKeyType = segwit ? ScriptPubKeyType.Segwit : ScriptPubKeyType.Legacy,
                SavePrivateKeys = importKeysToNBX
            });

            await store.AddDerivationScheme(StoreId, new DerivationSchemeViewModel()
            {
                Enabled = true,
                CryptoCode = cryptoCode,
                Network = SupportedNetwork,
                RootFingerprint = GenerateWalletResponseV.AccountKeyPath.MasterFingerprint.ToString(),
                RootKeyPath = SupportedNetwork.GetRootKeyPath(),
                Source = "NBXplorer",
                AccountKey = GenerateWalletResponseV.AccountHDKey.Neuter().ToWif(),
                DerivationSchemeFormat = "BTCPay",
                KeyPath = GenerateWalletResponseV.AccountKeyPath.KeyPath.ToString(),
                DerivationScheme = DerivationScheme.ToString(),
                Confirmation = true
            }, cryptoCode);
            return new WalletId(StoreId, cryptoCode);
        }

        public GenerateWalletResponse GenerateWalletResponseV { get; set; }

        public DerivationStrategyBase DerivationScheme
        {
            get
            {
                return GenerateWalletResponseV.DerivationScheme;
            }
        }

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
            IsAdmin = account.RegisteredAdmin;
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
    }
}
