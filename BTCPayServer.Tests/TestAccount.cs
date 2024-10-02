using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Models.AccountViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payments.PayJoin.Sender;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Payment;
using NBitpayClient;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using Xunit;
using Xunit.Sdk;

namespace BTCPayServer.Tests
{
    public class TestAccount
    {
        readonly ServerTester parent;
        public string LNAddress;

        public TestAccount(ServerTester parent)
        {
            this.parent = parent;
            BitPay = new Bitpay(new Key(), parent.PayTester.ServerUri);
        }

        public void GrantAccess(bool isAdmin = false)
        {
            GrantAccessAsync(isAdmin).GetAwaiter().GetResult();
        }

        public async Task MakeAdmin(bool isAdmin = true)
        {
            var userManager = parent.PayTester.GetService<UserManager<ApplicationUser>>();
            var u = await userManager.FindByIdAsync(UserId);
            if (isAdmin)
                await userManager.AddToRoleAsync(u, Roles.ServerAdmin);
            else
                await userManager.RemoveFromRoleAsync(u, Roles.ServerAdmin);
            IsAdmin = true;
        }

        public Task<BTCPayServerClient> CreateClient()
        {
            return Task.FromResult(new BTCPayServerClient(parent.PayTester.ServerUri, RegisterDetails.Email,
                RegisterDetails.Password));
        }

        public async Task<BTCPayServerClient> CreateClient(params string[] permissions)
        {
            var manageController = parent.PayTester.GetController<UIManageController>(UserId, StoreId, IsAdmin);
            Assert.IsType<RedirectToActionResult>(await manageController.AddApiKey(
                new UIManageController.AddApiKeyViewModel()
                {
                    PermissionValues = permissions.Select(s =>
                    {
                        Permission.TryParse(s, out var p);
                        return p;
                    }).GroupBy(permission => permission.Policy).Select(p =>
                    {
                        var stores = p.Where(permission => !string.IsNullOrEmpty(permission.Scope))
                            .Select(permission => permission.Scope).ToList();
                        return new UIManageController.AddApiKeyViewModel.PermissionValueItem()
                        {
                            Permission = p.Key,
                            Forbidden = false,
                            StoreMode = stores.Any() ? UIManageController.AddApiKeyViewModel.ApiKeyStoreMode.Specific : UIManageController.AddApiKeyViewModel.ApiKeyStoreMode.AllStores,
                            SpecificStores = stores,
                            Value = true
                        };
                    }).ToList()
                }));
            var statusMessage = manageController.TempData.GetStatusMessageModel();
            Assert.NotNull(statusMessage);
            var str = "<code class='alert-link'>";
            var apiKey = statusMessage.Html.Substring(statusMessage.Html.IndexOf(str) + str.Length);
            apiKey = apiKey.Substring(0, apiKey.IndexOf("</code>"));
            return new BTCPayServerClient(parent.PayTester.ServerUri, apiKey);
        }

        public void Register(bool isAdmin = false)
        {
            RegisterAsync(isAdmin).GetAwaiter().GetResult();
        }

        public async Task GrantAccessAsync(bool isAdmin = false)
        {
            await RegisterAsync(isAdmin);
            await CreateStoreAsync();
            var store = GetController<UIStoresController>();
            var pairingCode = BitPay.RequestClientAuthorization("test", Facade.Merchant);
            Assert.IsType<ViewResult>(await store.RequestPairing(pairingCode.ToString()));
            await store.Pair(pairingCode.ToString(), StoreId);
        }

        public BTCPayServerClient CreateClientFromAPIKey(string apiKey)
        {
            return new BTCPayServerClient(parent.PayTester.ServerUri, apiKey);
        }

        public void CreateStore()
        {
            CreateStoreAsync().GetAwaiter().GetResult();
        }

        public async Task SetNetworkFeeMode(NetworkFeeMode mode)
        {
            await ModifyPayment(payment =>
            {
                payment.NetworkFeeMode = mode;
            });
        }

        public async Task ModifyPayment(Action<GeneralSettingsViewModel> modify)
        {
            var storeController = GetController<UIStoresController>();
            var response = await storeController.GeneralSettings(StoreId);
            GeneralSettingsViewModel settings = (GeneralSettingsViewModel)((ViewResult)response).Model!;
            modify(settings);
            await storeController.GeneralSettings(settings);
        }

        public async Task ModifyGeneralSettings(Action<GeneralSettingsViewModel> modify)
        {
            var storeController = GetController<UIStoresController>();
            var response = await storeController.GeneralSettings(StoreId);
            GeneralSettingsViewModel settings = (GeneralSettingsViewModel)((ViewResult)response).Model!;
            modify(settings);
            storeController.GeneralSettings(settings).GetAwaiter().GetResult();
        }

        public async Task ModifyOnchainPaymentSettings(Action<WalletSettingsViewModel> modify)
        {
            var storeController = GetController<UIStoresController>();
            var response = await storeController.WalletSettings(StoreId, "BTC");
            WalletSettingsViewModel walletSettings = (WalletSettingsViewModel)((ViewResult)response).Model;
            modify(walletSettings);
            storeController.UpdateWalletSettings(walletSettings).GetAwaiter().GetResult();
        }

        public T GetController<T>(bool setImplicitStore = true) where T : Controller
        {
            var controller = parent.PayTester.GetController<T>(UserId, setImplicitStore ? StoreId : null, IsAdmin);
            return controller;
        }

        public async Task CreateStoreAsync()
        {
            if (UserId is null)
            {
                await RegisterAsync();
            }
            var store = GetController<UIUserStoresController>();
            await store.CreateStore(new CreateStoreViewModel { Name = "Test Store", PreferredExchange = "coingecko" });
            StoreId = store.CreatedStoreId;
            parent.Stores.Add(StoreId);
        }

        public BTCPayNetwork SupportedNetwork { get; set; }

        public WalletId RegisterDerivationScheme(string crytoCode, ScriptPubKeyType segwit = ScriptPubKeyType.Legacy, bool importKeysToNBX = false)
        {
            return RegisterDerivationSchemeAsync(crytoCode, segwit, importKeysToNBX).GetAwaiter().GetResult();
        }

        public async Task<WalletId> RegisterDerivationSchemeAsync(string cryptoCode, ScriptPubKeyType segwit = ScriptPubKeyType.Legacy,
            bool importKeysToNBX = false, bool importsKeysToBitcoinCore = false)
        {
            if (StoreId is null)
                await CreateStoreAsync();
            SupportedNetwork = parent.NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            var store = parent.PayTester.GetController<UIStoresController>(UserId, StoreId, true);

            var generateRequest = new WalletSetupRequest
            {
                ScriptPubKeyType = segwit,
                SavePrivateKeys = importKeysToNBX,
                ImportKeysToRPC = importsKeysToBitcoinCore
            };

            await store.GenerateWallet(StoreId, cryptoCode, WalletSetupMethod.HotWallet, generateRequest);
            Assert.NotNull(store.GenerateWalletResponse);
            GenerateWalletResponseV = store.GenerateWalletResponse;
            return new WalletId(StoreId, cryptoCode);
        }

        public GenerateWalletResponse GenerateWalletResponseV { get; set; }

        public DerivationStrategyBase DerivationScheme
        {
            get => GenerateWalletResponseV.DerivationScheme;
        }

        public void SetLNUrl(string cryptoCode, bool activated)
        {
            var lnSettingsVm = GetController<UIStoresController>().LightningSettings(StoreId, cryptoCode).AssertViewModel<LightningSettingsViewModel>();
            lnSettingsVm.LNURLEnabled = activated;
            Assert.IsType<RedirectToActionResult>(GetController<UIStoresController>().LightningSettings(lnSettingsVm).Result);
        }

        private async Task RegisterAsync(bool isAdmin = false)
        {
            var account = parent.PayTester.GetController<UIAccountController>();
            RegisterDetails = new RegisterViewModel()
            {
                Email = Utils.GenerateEmail(),
                ConfirmPassword = Password,
                Password = Password,
                IsAdmin = isAdmin
            };
            await account.Register(RegisterDetails);

            //this addresses an obscure issue where LockSubscription is unintentionally set to "true",
            //resulting in a large number of tests failing.  
            if (account.RegisteredUserId == null)
            {
                var settings = parent.PayTester.GetService<SettingsRepository>();
                var policies = await settings.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
                policies.LockSubscription = false;
                await account.Register(RegisterDetails);
            }
            TestLogs.LogInformation($"UserId: {account.RegisteredUserId} Password: {Password}");
            UserId = account.RegisteredUserId;
            Email = RegisterDetails.Email;
            IsAdmin = account.RegisteredAdmin;
        }
        public string Password { get; set; } = "Kitten0@";

        public RegisterViewModel RegisterDetails { get; set; }

        public Bitpay BitPay
        {
            get;
            set;
        }

        public string UserId
        {
            get;
            set;
        }

        public string Email
        {
            get;
            set;
        }

        public string StoreId
        {
            get;
            set;
        }

        public bool IsAdmin { get; internal set; }

        public void RegisterLightningNode(string cryptoCode, string connectionType = null, bool isMerchant = true)
        {
            RegisterLightningNodeAsync(cryptoCode, connectionType, isMerchant).GetAwaiter().GetResult();
        }
        public Task RegisterLightningNodeAsync(string cryptoCode, bool isMerchant = true, string storeId = null)
        {
            return RegisterLightningNodeAsync(cryptoCode, null, isMerchant, storeId);
        }
        public async Task RegisterLightningNodeAsync(string cryptoCode, string connectionType, bool isMerchant = true, string storeId = null)
        {
            var storeController = GetController<UIStoresController>();

            var connectionString = parent.GetLightningConnectionString(connectionType, isMerchant);
            var nodeType = connectionString == LightningPaymentMethodConfig.InternalNode ? LightningNodeType.Internal : LightningNodeType.Custom;

            var vm = new LightningNodeViewModel { ConnectionString = connectionString, LightningNodeType = nodeType, SkipPortTest = true };
            await storeController.SetupLightningNode(storeId ?? StoreId,
                vm, "save", cryptoCode);
            if (storeController.ModelState.ErrorCount != 0)
                Assert.Fail(storeController.ModelState.FirstOrDefault().Value.Errors[0].ErrorMessage);
        }

        public async Task RegisterInternalLightningNodeAsync(string cryptoCode, string storeId = null)
        {
            var storeController = GetController<UIStoresController>();
            var vm = new LightningNodeViewModel { ConnectionString = "", LightningNodeType = LightningNodeType.Internal, SkipPortTest = true };
            await storeController.SetupLightningNode(storeId ?? StoreId,
                vm, "save", cryptoCode);
            if (storeController.ModelState.ErrorCount != 0)
                Assert.Fail(storeController.ModelState.FirstOrDefault().Value.Errors[0].ErrorMessage);
        }

        public async Task<Coin> ReceiveUTXO(Money value, BTCPayNetwork network = null)
        {
            network ??= SupportedNetwork;
            var cashCow = parent.ExplorerNode;
            var btcPayWallet = parent.PayTester.GetService<BTCPayWalletProvider>().GetWallet(network);
            var address = (await btcPayWallet.ReserveAddressAsync(this.DerivationScheme)).Address;
            await parent.WaitForEvent<NewOnChainTransactionEvent>(async () =>
            {
                await cashCow.SendToAddressAsync(address, value);
            });
            int i = 0;
            while (i < 30)
            {
                var result = (await btcPayWallet.GetUnspentCoins(DerivationScheme))
                    .FirstOrDefault(c => c.ScriptPubKey == address.ScriptPubKey)?.Coin;
                if (result != null)
                {
                    return result;
                }

                await Task.Delay(1000);
                i++;
            }
            Assert.False(true);
            return null;
        }

        public async Task<BitcoinAddress> GetNewAddress(BTCPayNetwork network)
        {
            var btcPayWallet = parent.PayTester.GetService<BTCPayWalletProvider>().GetWallet(network);
            var address = (await btcPayWallet.ReserveAddressAsync(this.DerivationScheme)).Address;
            return address;
        }

        public async Task<PSBT> Sign(PSBT psbt)
        {
            parent.PayTester.GetService<BTCPayWalletProvider>()
                .GetWallet(psbt.Network.NetworkSet.CryptoCode);
            var explorerClient = parent.PayTester.GetService<ExplorerClientProvider>()
                .GetExplorerClient(psbt.Network.NetworkSet.CryptoCode);
            psbt = (await explorerClient.UpdatePSBTAsync(new UpdatePSBTRequest()
            {
                DerivationScheme = DerivationScheme,
                PSBT = psbt
            })).PSBT;
            return psbt.SignAll(this.DerivationScheme, GenerateWalletResponseV.AccountHDKey,
                GenerateWalletResponseV.AccountKeyPath);
        }
        Logging.ILog TestLogs => this.parent.TestLogs;
        public async Task<PSBT> SubmitPayjoin(Invoice invoice, PSBT psbt, string expectedError = null, bool senderError = false)
        {
            var endpoint = GetPayjoinBitcoinUrl(invoice, psbt.Network);
            if (endpoint == null)
            {
                throw new InvalidOperationException("No payjoin endpoint for the invoice");
            }
            var pjClient = parent.PayTester.GetService<PayjoinClient>();
            var storeRepository = parent.PayTester.GetService<StoreRepository>();
            var store = await storeRepository.FindStore(StoreId);
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(psbt.Network.NetworkSet.CryptoCode);
            var handlers = parent.PayTester.GetService<PaymentMethodHandlerDictionary>();
            var settings = store.GetPaymentMethodConfig<DerivationSchemeSettings>(pmi, handlers);
            TestLogs.LogInformation($"Proposing {psbt.GetGlobalTransaction().GetHash()}");
            if (expectedError is null && !senderError)
            {
                var proposed = await pjClient.RequestPayjoin(endpoint, new PayjoinWallet(settings), psbt, default);
                TestLogs.LogInformation($"Proposed payjoin is {proposed.GetGlobalTransaction().GetHash()}");
                Assert.NotNull(proposed);
                return proposed;
            }
            else
            {
                if (senderError)
                {
                    await Assert.ThrowsAsync<PayjoinSenderException>(async () => await pjClient.RequestPayjoin(endpoint, new PayjoinWallet(settings), psbt, default));
                }
                else
                {
                    var ex = await Assert.ThrowsAsync<PayjoinReceiverException>(async () => await pjClient.RequestPayjoin(endpoint, new PayjoinWallet(settings), psbt, default));
                    var split = expectedError.Split('|');
                    Assert.Equal(split[0], ex.ErrorCode);
                    if (split.Length > 1)
                        Assert.Contains(split[1], ex.ReceiverMessage);
                }
                return null;
            }
        }

        public async Task<Transaction> SubmitPayjoin(Invoice invoice, Transaction transaction, BTCPayNetwork network,
            string expectedError = null)
        {
            var response =
                await SubmitPayjoinCore(transaction.ToHex(), invoice, network.NBitcoinNetwork, expectedError);
            if (response == null)
                return null;
            var signed = Transaction.Parse(await response.Content.ReadAsStringAsync(), network.NBitcoinNetwork);
            return signed;
        }

        async Task<HttpResponseMessage> SubmitPayjoinCore(string content, Invoice invoice, Network network,
            string expectedError)
        {
            var bip21 = GetPayjoinBitcoinUrl(invoice, network);
            bip21.TryGetPayjoinEndpoint(out var endpoint);
            var response = await parent.PayTester.HttpClient.PostAsync(endpoint,
                new StringContent(content, Encoding.UTF8, "text/plain"));
            if (expectedError != null)
            {
                var split = expectedError.Split('|');
                Assert.False(response.IsSuccessStatusCode);
                var error = JObject.Parse(await response.Content.ReadAsStringAsync());
                if (split.Length > 0)
                    Assert.Equal(split[0], error["errorCode"].Value<string>());
                if (split.Length > 1)
                    Assert.Contains(split[1], error["message"].Value<string>());
                return null;
            }
            else
            {
                if (!response.IsSuccessStatusCode)
                {
                    var error = JObject.Parse(await response.Content.ReadAsStringAsync());
                    Assert.Fail($"Error: {error["errorCode"].Value<string>()}: {error["message"].Value<string>()}");
                }
            }

            return response;
        }

        public static BitcoinUrlBuilder GetPayjoinBitcoinUrl(Invoice invoice, Network network)
        {
            var parsedBip21 = new BitcoinUrlBuilder(
                invoice.CryptoInfo.First(c => c.CryptoCode == network.NetworkSet.CryptoCode).PaymentUrls.BIP21,
                network);
            if (!parsedBip21.TryGetPayjoinEndpoint(out _))
                return null;
            return parsedBip21;
        }

        class WebhookListener : IDisposable
        {
            private Client.Models.StoreWebhookData _wh;
            private FakeServer _server;
            private readonly List<StoreWebhookEvent> _webhookEvents;
            private CancellationTokenSource _cts;
            public WebhookListener(Client.Models.StoreWebhookData wh, FakeServer server, List<StoreWebhookEvent> webhookEvents)
            {
                _wh = wh;
                _server = server;
                _webhookEvents = webhookEvents;
                _cts = new CancellationTokenSource();
                _ = Listen(_cts.Token);
            }

            async Task Listen(CancellationToken cancellation)
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var req = await _server.GetNextRequest(cancellation);
                    var bytes = await req.Request.Body.ReadBytesAsync((int)req.Request.Headers.ContentLength);
                    var callback = Encoding.UTF8.GetString(bytes);
                    lock (_webhookEvents)
                    {
                        _webhookEvents.Add(JsonConvert.DeserializeObject<DummyStoreWebhookEvent>(callback));
                    }
                    req.Response.StatusCode = 200;
                    _server.Done();
                }
            }
            public void Dispose()
            {
                _cts.Cancel();
                _server.Dispose();
            }
        }

        public class DummyStoreWebhookEvent : StoreWebhookEvent
        {

        }

        public List<StoreWebhookEvent> WebhookEvents { get; set; } = new List<StoreWebhookEvent>();
        public async Task<TEvent> AssertHasWebhookEvent<TEvent>(string eventType, Action<TEvent> assert) where TEvent : class
        {
            int retry = 0;
retry:
            lock (WebhookEvents)
            {
                foreach (var evt in WebhookEvents)
                {
                    if (evt.Type == eventType)
                    {
                        var typedEvt = evt.ReadAs<TEvent>();
                        try
                        {
                            assert(typedEvt);
                            return typedEvt;
                        }
                        catch (XunitException)
                        {
                        }
                    }
                }
            }
            if (retry < 3)
            {
                await Task.Delay(1000);
                retry++;
                goto retry;
            }
            Assert.Fail("No webhook event match the assertion");
            return null;
        }
        public async Task SetupWebhook()
        {
            var server = new FakeServer();
            await server.Start();
            var client = await CreateClient(Policies.CanModifyWebhooks);
            var wh = await client.CreateWebhook(StoreId, new CreateStoreWebhookRequest()
            {
                AutomaticRedelivery = false,
                Url = server.ServerUri.AbsoluteUri
            });

            parent.Resources.Add(new WebhookListener(wh, server, WebhookEvents));
        }

        public async Task PayInvoice(string invoiceId)
        {
            var inv = await BitPay.GetInvoiceAsync(invoiceId);
            var net = parent.ExplorerNode.Network;
            await parent.ExplorerNode.SendToAddressAsync(BitcoinAddress.Create(inv.BitcoinAddress, net), inv.BtcDue);
            await TestUtils.EventuallyAsync(async () =>
            {
                var localInvoice = await BitPay.GetInvoiceAsync(invoiceId, Facade.Merchant);
                Assert.Equal("paid", localInvoice.Status);
            });
        }

        public async Task AddGuest(string userId)
        {
            var repo = parent.PayTester.GetService<StoreRepository>();
            await repo.AddOrUpdateStoreUser(StoreId, userId, StoreRoleId.Guest);
        }
        public async Task AddOwner(string userId)
        {
            var repo = parent.PayTester.GetService<StoreRepository>();
            await repo.AddOrUpdateStoreUser(StoreId, userId, StoreRoleId.Owner);
        }
        public async Task AddManager(string userId)
        {
            var repo = parent.PayTester.GetService<StoreRepository>();
            await repo.AddOrUpdateStoreUser(StoreId, userId, StoreRoleId.Manager);
        }
        public async Task AddEmployee(string userId)
        {
            var repo = parent.PayTester.GetService<StoreRepository>();
            await repo.AddOrUpdateStoreUser(StoreId, userId, StoreRoleId.Employee);
        }

        public async Task<uint256> PayOnChain(string invoiceId)
        {
            var cryptoCode = "BTC";
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var client = await CreateClient();
            var methods = await client.GetInvoicePaymentMethods(StoreId, invoiceId);
            var method = methods.First(m => m.PaymentMethodId == pmi.ToString());
            var address = method.Destination;
            var tx = await client.CreateOnChainTransaction(StoreId, cryptoCode, new CreateOnChainTransactionRequest()
            {
                Destinations = new List<CreateOnChainTransactionRequest.CreateOnChainTransactionRequestDestination>()
                {
                 new ()
                 {
                     Destination = address,
                     Amount = method.Due
                 }
                },
                FeeRate = new FeeRate(1.0m)
            });
            await WaitInvoicePaid(invoiceId);
            return tx.TransactionHash;
        }

        public async Task PayOnBOLT11(string invoiceId)
        {
            var cryptoCode = "BTC";
            var client = await CreateClient();
            var methods = await client.GetInvoicePaymentMethods(StoreId, invoiceId);
            var method = methods.First(m => m.PaymentMethodId == $"{cryptoCode}-LN");
            var bolt11 = method.Destination;
            TestLogs.LogInformation("PAYING");
            await parent.CustomerLightningD.Pay(bolt11);
            TestLogs.LogInformation("PAID");
            await WaitInvoicePaid(invoiceId);
        }

        public async Task PayOnLNUrl(string invoiceId)
        {
            var cryptoCode = "BTC";
            var network = SupportedNetwork.NBitcoinNetwork;
            var client = await CreateClient();
            var methods = await client.GetInvoicePaymentMethods(StoreId, invoiceId);
            var method = methods.First(m => m.PaymentMethodId == $"{cryptoCode}-LNURL");
            var lnurL = LNURL.LNURL.Parse(method.PaymentLink, out var tag);
            var http = new HttpClient();
            var payreq = (LNURL.LNURLPayRequest)await LNURL.LNURL.FetchInformation(lnurL, tag, http);
            var resp = await payreq.SendRequest(payreq.MinSendable, network, http);
            var bolt11 = resp.Pr;
            await parent.CustomerLightningD.Pay(bolt11);
            await WaitInvoicePaid(invoiceId);
        }

        public Task WaitInvoicePaid(string invoiceId)
        {
            return TestUtils.EventuallyAsync(async () =>
            {
                var client = await CreateClient();
                var invoice = await client.GetInvoice(StoreId, invoiceId);
                if (invoice.Status == InvoiceStatus.Settled)
                    return;
                Assert.Equal(InvoiceStatus.Processing, invoice.Status);
            });
        }

        public async Task PayOnLNAddress(string lnAddrUser = null)
        {
            lnAddrUser ??= LNAddress;
            var network = SupportedNetwork.NBitcoinNetwork;
            var payReqStr = await (await parent.PayTester.HttpClient.GetAsync($".well-known/lnurlp/{lnAddrUser}")).Content.ReadAsStringAsync();
            var payreq = JsonConvert.DeserializeObject<LNURL.LNURLPayRequest>(payReqStr);
            var resp = await payreq.SendRequest(payreq.MinSendable, network, parent.PayTester.HttpClient);
            var bolt11 = resp.Pr;
            await parent.CustomerLightningD.Pay(bolt11);
        }

        public async Task<string> CreateLNAddress()
        {
            var lnAddrUser = Guid.NewGuid().ToString();
            var ctx = parent.PayTester.GetService<ApplicationDbContextFactory>().CreateContext();
            ctx.LightningAddresses.Add(new()
            {
                StoreDataId = StoreId,
                Username = lnAddrUser
            });
            await ctx.SaveChangesAsync();
            LNAddress = lnAddrUser;
            return lnAddrUser;
        }

        public async Task ImportOldInvoices(string storeId = null)
        {
            storeId ??= StoreId;
            var oldInvoices = File.ReadAllLines(TestUtils.GetTestDataFullPath("OldInvoices.csv"));
            var oldPayments = File.ReadAllLines(TestUtils.GetTestDataFullPath("OldPayments.csv"));
            var dbContext = this.parent.PayTester.GetService<ApplicationDbContextFactory>().CreateContext();
            var db = (NpgsqlConnection)dbContext.Database.GetDbConnection();
            await db.OpenAsync();
            bool isHeader = true;
            using (var writer = db.BeginTextImport("COPY \"Invoices\" (\"Id\",\"Blob\",\"Created\",\"ExceptionStatus\",\"Status\",\"StoreDataId\",\"Archived\",\"Blob2\") FROM STDIN DELIMITER ',' CSV HEADER"))
            {
                foreach (var invoice in oldInvoices)
                {
                    if (isHeader)
                    {
                        isHeader = false;
                        await writer.WriteLineAsync(invoice);
                    }
                    else
                    {
                        var localInvoice = invoice.Replace("3sgUCCtUBg6S8LJkrbdfAWbsJMqByFLfvSqjG6xKBWEd", storeId);
                        var fields = localInvoice.Split(',');
                        var blob1 = ZipUtils.Unzip(Encoders.Hex.DecodeData(fields[1].Substring(2)));
                        var matched = Regex.Match(blob1, "xpub[^\\\"-]*");
                        if (matched.Success)
                        {
                            var xpub = (BitcoinExtPubKey)Network.Main.Parse(matched.Value);
                            var xpubTestnet = xpub.ExtPubKey.GetWif(Network.RegTest).ToString();
                            blob1 = blob1.Replace(xpub.ToString(), xpubTestnet.ToString());
                            fields[1] = $"\\x{Encoders.Hex.EncodeData(ZipUtils.Zip(blob1))}";
                            localInvoice = string.Join(',', fields);
                        }
                        await writer.WriteLineAsync(localInvoice);
                    }
                }
                await writer.FlushAsync();
            }
            isHeader = true;
            using (var writer = db.BeginTextImport("COPY \"Payments\" (\"Id\",\"Blob\",\"InvoiceDataId\",\"Accounted\",\"Blob2\",\"PaymentMethodId\") FROM STDIN DELIMITER ',' CSV HEADER"))
            {
                foreach (var invoice in oldPayments)
                {
                    var localPayment = invoice.Replace("3sgUCCtUBg6S8LJkrbdfAWbsJMqByFLfvSqjG6xKBWEd", storeId);
                    // Old data could have Type to null.
                    localPayment += "UNKNOWN";
                    await writer.WriteLineAsync(localPayment);
                }
                await writer.FlushAsync();
            }
        }
    }
}
