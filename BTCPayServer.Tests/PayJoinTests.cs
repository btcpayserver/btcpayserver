using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.PayJoin;
using BTCPayServer.Payments.PayJoin.Sender;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Tests.Logging;
using BTCPayServer.Views.Stores;
using BTCPayServer.Views.Wallets;
using Microsoft.AspNetCore.Http;
using NBitcoin;
using NBitcoin.Payment;
using NBitpayClient;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.Extensions;
using OpenQA.Selenium.Support.UI;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class PayJoinTests : UnitTestBase
    {
        public const int TestTimeout = 60_000;

        public PayJoinTests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanUseTheDelayedBroadcaster()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var network = tester.NetworkProvider.GetNetwork<BTCPayNetwork>("BTC");
            var broadcaster = tester.PayTester.GetService<DelayedTransactionBroadcaster>();
            await broadcaster.Schedule(DateTimeOffset.UtcNow + TimeSpan.FromDays(500), RandomTransaction(network), network);
            var tx = RandomTransaction(network);
            await broadcaster.Schedule(DateTimeOffset.UtcNow - TimeSpan.FromDays(5), tx, network);
            // twice on same tx should be noop
            await broadcaster.Schedule(DateTimeOffset.UtcNow - TimeSpan.FromDays(5), tx, network);
            broadcaster.Disable();
            Assert.Equal(0, await broadcaster.ProcessAll());
            broadcaster.Enable();
            Assert.Equal(1, await broadcaster.ProcessAll());
            Assert.Equal(0, await broadcaster.ProcessAll());
        }
        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanUsePayjoinRepository()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            tester.NetworkProvider.GetNetwork<BTCPayNetwork>("BTC");
            var repo = tester.PayTester.GetService<UTXOLocker>();
            var outpoint = RandomOutpoint();

            // Should not be locked
            Assert.False(await repo.TryUnlock(outpoint));

            // Can lock input
            Assert.True(await repo.TryLockInputs(new[] { outpoint }));
            // Can't twice
            Assert.False(await repo.TryLockInputs(new[] { outpoint }));
            Assert.False(await repo.TryUnlock(outpoint));

            // Lock and unlock outpoint utxo
            Assert.True(await repo.TryLock(outpoint));
            Assert.True(await repo.TryUnlock(outpoint));
            Assert.False(await repo.TryUnlock(outpoint));

            // Make sure that if any can't be locked, all are not locked
            var outpoint1 = RandomOutpoint();
            var outpoint2 = RandomOutpoint();
            Assert.True(await repo.TryLockInputs(new[] { outpoint1 }));
            Assert.False(await repo.TryLockInputs(new[] { outpoint1, outpoint2 }));
            Assert.True(await repo.TryLockInputs(new[] { outpoint2 }));

            outpoint1 = RandomOutpoint();
            outpoint2 = RandomOutpoint();
            Assert.True(await repo.TryLockInputs(new[] { outpoint1 }));
            Assert.False(await repo.TryLockInputs(new[] { outpoint2, outpoint1 }));
            Assert.True(await repo.TryLockInputs(new[] { outpoint2 }));
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task ChooseBestUTXOsForPayjoin()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var network = tester.NetworkProvider.GetNetwork<BTCPayNetwork>("BTC");
            var controller = tester.PayTester.GetService<PayJoinEndpointController>();

            var utxos = new[] { FakeUTXO(1m) };
            var paymentAmount = 0.5m;
            var otherOutputs = new[] { 0.5m };
            var inputs = new[] { 1m };
            var result = await controller.SelectUTXO(network, utxos, inputs, paymentAmount, otherOutputs);
            Assert.Equal(PayJoinEndpointController.PayjoinUtxoSelectionType.HeuristicBased, result.selectionType);
            Assert.Contains(result.selectedUTXO, utxo => utxos.Contains(utxo));

            //no matter what here, no good selection, it seems that payment with 1 utxo generally makes payjoin coin selection unperformant
            utxos = new[] { FakeUTXO(0.3m), FakeUTXO(0.7m) };
            paymentAmount = 0.5m;
            otherOutputs = new[] { 0.5m };
            inputs = new[] { 1m };
            result = await controller.SelectUTXO(network, utxos, inputs, paymentAmount, otherOutputs);
            Assert.Equal(PayJoinEndpointController.PayjoinUtxoSelectionType.HeuristicBased, result.selectionType);

            //when there is no change, anything works
            utxos = new[] { FakeUTXO(1), FakeUTXO(0.1m), FakeUTXO(0.001m), FakeUTXO(0.003m) };
            paymentAmount = 0.5m;
            otherOutputs = new decimal[0];
            inputs = new[] { 0.03m, 0.07m };
            result = await controller.SelectUTXO(network, utxos, inputs, paymentAmount, otherOutputs);


            // We want to make a transaction such that
            // min(out) < min(in)

            // Original transaction is:
            // 0.5      -> 0.3 , 0.1
            // When chosing a new utxo x, we have the modified tx
            // 0.5 , x -> 0.3 , (0.1+x)
            // We need:
            // min(0.3, 0.1+x) < min(0.5, x)
            // Any x > 0.3 should be fine
            utxos = new[] { FakeUTXO(0.2m), FakeUTXO(0.3m), FakeUTXO(0.31m) };
            paymentAmount = 0.1m;
            otherOutputs = new decimal[] { 0.3m };
            inputs = new[] { 0.5m };
            result = await controller.SelectUTXO(network, utxos, inputs, paymentAmount, otherOutputs);
            Assert.Equal(PayJoinEndpointController.PayjoinUtxoSelectionType.HeuristicBased, result.selectionType);
            Assert.Equal(0.31m, result.selectedUTXO[0].Value.GetValue(network));

            // If the 0.31m wasn't available, no selection heuristic based
            utxos = new[] { FakeUTXO(0.2m), FakeUTXO(0.3m) };
            result = await controller.SelectUTXO(network, utxos, inputs, paymentAmount, otherOutputs);
            Assert.Equal(PayJoinEndpointController.PayjoinUtxoSelectionType.Ordered, result.selectionType);
            Assert.Equal(0.2m, result.selectedUTXO[0].Value.GetValue(network));
        }



        private Transaction RandomTransaction(BTCPayNetwork network)
        {
            var tx = network.NBitcoinNetwork.CreateTransaction();
            tx.Inputs.Add(new OutPoint(RandomUtils.GetUInt256(), 0), Script.Empty);
            tx.Outputs.Add(Money.Coins(1.0m), new Key().GetScriptPubKey(ScriptPubKeyType.Legacy));
            return tx;
        }

        private UTXO FakeUTXO(decimal amount)
        {
            return new UTXO()
            {
                Value = new Money(amount, MoneyUnit.BTC),
                Outpoint = RandomOutpoint()
            };
        }

        private OutPoint RandomOutpoint()
        {
            return new OutPoint(RandomUtils.GetUInt256(), 0);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanOnlyUseCorrectAddressFormatsForPayjoin()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var broadcaster = tester.PayTester.GetService<DelayedTransactionBroadcaster>();
            tester.PayTester.GetService<UTXOLocker>();
            broadcaster.Disable();
            var network = tester.NetworkProvider.GetNetwork<BTCPayNetwork>("BTC");
            tester.PayTester.GetService<BTCPayWalletProvider>().GetWallet(network);
            var cashCow = tester.ExplorerNode;
            cashCow.Generate(2); // get some money in case

            var unsupportedFormats = new[] { ScriptPubKeyType.Legacy };


            foreach (ScriptPubKeyType senderAddressType in Enum.GetValues(typeof(ScriptPubKeyType)))
            {
                if (senderAddressType == ScriptPubKeyType.TaprootBIP86)
                    continue;
                var senderUser = tester.NewAccount();
                senderUser.GrantAccess(true);
                senderUser.RegisterDerivationScheme("BTC", senderAddressType);

                foreach (ScriptPubKeyType receiverAddressType in Enum.GetValues(typeof(ScriptPubKeyType)))
                {
                    if (receiverAddressType == ScriptPubKeyType.TaprootBIP86)
                        continue;
                    var senderCoin = await senderUser.ReceiveUTXO(Money.Satoshis(100000), network);

                    TestLogs.LogInformation($"Testing payjoin with sender: {senderAddressType} receiver: {receiverAddressType}");
                    var receiverUser = tester.NewAccount();
                    receiverUser.GrantAccess(true);
                    receiverUser.RegisterDerivationScheme("BTC", receiverAddressType, true);
                    await receiverUser.ModifyOnchainPaymentSettings(p => p.PayJoinEnabled = true);
                    await receiverUser.ReceiveUTXO(Money.Satoshis(810), network);

                    string errorCode = receiverAddressType == senderAddressType ? null : "unavailable|any UTXO available";
                    var invoice = receiverUser.BitPay.CreateInvoice(new Invoice() { Price = 50000, Currency = "SATS", FullNotifications = true });
                    if (unsupportedFormats.Contains(receiverAddressType))
                    {
                        Assert.Null(TestAccount.GetPayjoinBitcoinUrl(invoice, cashCow.Network));
                        continue;
                    }
                    var invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network);
                    var txBuilder = network.NBitcoinNetwork.CreateTransactionBuilder();

                    txBuilder.AddCoins(senderCoin);
                    txBuilder.Send(invoiceAddress, invoice.BtcDue);
                    txBuilder.SetChange(await senderUser.GetNewAddress(network));
                    txBuilder.SendEstimatedFees(new FeeRate(50m));
                    var psbt = txBuilder.BuildPSBT(false);
                    psbt = await senderUser.Sign(psbt);
                    await senderUser.SubmitPayjoin(invoice, psbt, errorCode, false);
                }
            }
        }

        [Fact]
        [Trait("Selenium", "Selenium")]
        public async Task CanUsePayjoinForTopUp()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.RegisterNewUser(true);
            var receiver = s.CreateNewStore();
            s.GenerateWallet("BTC", "", true, true);
            var receiverWalletId = new WalletId(receiver.storeId, "BTC");

            var sender = s.CreateNewStore();
            s.GenerateWallet("BTC", "", true, true);
            var senderWalletId = new WalletId(sender.storeId, "BTC");

            await s.Server.ExplorerNode.GenerateAsync(1);
            await s.FundStoreWallet(senderWalletId);
            await s.FundStoreWallet(receiverWalletId);

            var invoiceId = s.CreateInvoice(receiver.storeId, null, "BTC");
            s.GoToInvoiceCheckout(invoiceId);
            var bip21 = s.Driver.WaitForElement(By.Id("PayInWallet")).GetAttribute("href");
            Assert.Contains($"{PayjoinClient.BIP21EndpointKey}=", bip21);
            s.GoToWallet(senderWalletId, WalletsNavPages.Send);
            s.Driver.FindElement(By.Id("bip21parse")).Click();
            s.Driver.SwitchTo().Alert().SendKeys(bip21);
            s.Driver.SwitchTo().Alert().Accept();
            s.Driver.FindElementUntilNotStaled(By.Id("Outputs_0__Amount"), we => we.Clear());
            s.Driver.FindElementUntilNotStaled(By.Id("Outputs_0__Amount"), we => we.SendKeys("0.023"));

            s.Driver.FindElement(By.Id("SignTransaction")).Click();

            await s.Server.WaitForEvent<NewOnChainTransactionEvent>(() =>
            {
                s.Driver.FindElement(By.CssSelector("button[value=payjoin]")).Click();
                return Task.CompletedTask;
            });

            s.FindAlertMessage(StatusMessageModel.StatusSeverity.Success);
            var invoiceRepository = s.Server.PayTester.GetService<InvoiceRepository>();
            await TestUtils.EventuallyAsync(async () =>
            {
                var invoice = await invoiceRepository.GetInvoice(invoiceId);
                Assert.Equal(InvoiceStatus.Processing, invoice.Status);
                Assert.Equal(0.023m, invoice.Price);
            });
        }

        [Fact]
        [Trait("Selenium", "Selenium")]
        public async Task CanUsePayjoinViaUI()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            var invoiceRepository = s.Server.PayTester.GetService<InvoiceRepository>();
            s.RegisterNewUser(true);

            foreach (var format in new[] { ScriptPubKeyType.Segwit, ScriptPubKeyType.SegwitP2SH })
            {
                var cryptoCode = "BTC";
                var receiver = s.CreateNewStore();
                s.GenerateWallet(cryptoCode, "", true, true, format);
                var receiverWalletId = new WalletId(receiver.storeId, cryptoCode);

                //payjoin is enabled by default.
                var invoiceId = s.CreateInvoice(receiver.storeId);
                s.GoToInvoiceCheckout(invoiceId);
                var bip21 = s.Driver.WaitForElement(By.Id("PayInWallet")).GetAttribute("href");
                Assert.Contains($"{PayjoinClient.BIP21EndpointKey}=", bip21);

                s.GoToStore(receiver.storeId);
                s.GoToWalletSettings(cryptoCode);
                Assert.True(s.Driver.FindElement(By.Id("PayJoinEnabled")).Selected);

                var sender = s.CreateNewStore();
                s.GenerateWallet(cryptoCode, "", true, true, format);
                var senderWalletId = new WalletId(sender.storeId, cryptoCode);
                await s.Server.ExplorerNode.GenerateAsync(1);
                await s.FundStoreWallet(senderWalletId);

                invoiceId = s.CreateInvoice(receiver.storeId);
                s.GoToInvoiceCheckout(invoiceId);
                bip21 = s.Driver.WaitForElement(By.Id("PayInWallet")).GetAttribute("href");
                Assert.Contains($"{PayjoinClient.BIP21EndpointKey}=", bip21);

                s.GoToWallet(senderWalletId, WalletsNavPages.Send);
                s.Driver.FindElement(By.Id("bip21parse")).Click();
                s.Driver.SwitchTo().Alert().SendKeys(bip21);
                s.Driver.SwitchTo().Alert().Accept();
                Assert.False(string.IsNullOrEmpty(s.Driver.FindElement(By.Id("PayJoinBIP21"))
                    .GetAttribute("value")));
                s.Driver.FindElement(By.Id("SignTransaction")).Click();
                await s.Server.WaitForEvent<NewOnChainTransactionEvent>(() =>
                {
                    s.Driver.FindElement(By.CssSelector("button[value=payjoin]")).Click();
                    return Task.CompletedTask;
                });
                //no funds in receiver wallet to do payjoin
                s.FindAlertMessage(StatusMessageModel.StatusSeverity.Warning);
                await TestUtils.EventuallyAsync(async () =>
                {
                    var invoice = await s.Server.PayTester.GetService<InvoiceRepository>().GetInvoice(invoiceId);
                    Assert.Equal(InvoiceStatus.Processing, invoice.Status);
                });

                s.SelectStoreContext(receiver.storeId);
                s.GoToInvoices();
                var paymentValueRowColumn = s.Driver.FindElement(By.Id($"invoice_details_{invoiceId}"))
                    .FindElement(By.ClassName("payment-value"));
                Assert.False(paymentValueRowColumn.Text.Contains("payjoin",
                    StringComparison.InvariantCultureIgnoreCase));

                //let's do it all again, except now the receiver has funds and is able to payjoin
                invoiceId = s.CreateInvoice();
                s.GoToInvoiceCheckout(invoiceId);
                bip21 = s.Driver.WaitForElement(By.Id("PayInWallet")).GetAttribute("href");
                Assert.Contains($"{PayjoinClient.BIP21EndpointKey}", bip21);

                s.GoToWallet(senderWalletId, WalletsNavPages.Send);
                s.Driver.FindElement(By.Id("bip21parse")).Click();
                s.Driver.SwitchTo().Alert().SendKeys(bip21);
                s.Driver.SwitchTo().Alert().Accept();
                Assert.False(string.IsNullOrEmpty(s.Driver.FindElement(By.Id("PayJoinBIP21"))
                    .GetAttribute("value")));
                s.Driver.FindElement(By.Id("FeeSatoshiPerByte")).Clear();
                s.Driver.FindElement(By.Id("FeeSatoshiPerByte")).SendKeys("2");
                s.Driver.FindElement(By.Id("SignTransaction")).Click();
                await s.Server.WaitForEvent<NewOnChainTransactionEvent>(() =>
                {
                    s.Driver.FindElement(By.CssSelector("button[value=payjoin]")).Click();
                    return Task.CompletedTask;
                });
                s.FindAlertMessage();
                var handler = s.Server.PayTester.GetService<PaymentMethodHandlerDictionary>().GetBitcoinHandler("BTC");
                await TestUtils.EventuallyAsync(async () =>
                {
                    var invoice = await invoiceRepository.GetInvoice(invoiceId);
                    var payments = invoice.GetPayments(false);
                    Assert.Equal(2, payments.Count);
                    var originalPayment = payments[0];
                    var coinjoinPayment = payments[1];
                    Assert.Equal(-1,
                        handler.ParsePaymentDetails(originalPayment.Details).ConfirmationCount);
                    Assert.Equal(0,
                        handler.ParsePaymentDetails(coinjoinPayment.Details).ConfirmationCount);
                    Assert.False(originalPayment.Accounted);
                    Assert.True(coinjoinPayment.Accounted);
                    Assert.Equal(originalPayment.Value,
                        coinjoinPayment.Value);
                });

                await TestUtils.EventuallyAsync(async () =>
                {
                    var invoice = await s.Server.PayTester.GetService<InvoiceRepository>().GetInvoice(invoiceId);
                    Assert.Equal(InvoiceStatus.Processing, invoice.Status);
                });
                s.GoToInvoices(receiver.storeId);
                paymentValueRowColumn = s.Driver.FindElement(By.Id($"invoice_details_{invoiceId}"))
                    .FindElement(By.ClassName("payment-value"));
                Assert.False(paymentValueRowColumn.Text.Contains("payjoin",
                    StringComparison.InvariantCultureIgnoreCase));

                TestUtils.Eventually(() =>
                {
                    s.GoToWallet(receiverWalletId, WalletsNavPages.Transactions);
                    s.Driver.WaitForElement(By.CssSelector("#WalletTransactionsList tr"));
                    Assert.Contains("payjoin", s.Driver.PageSource);
                    // Either the invoice id or the payjoin-exposed label, depending on the input having been used
                    Assert.Matches(new Regex($"({invoiceId}|payjoin-exposed)"), s.Driver.PageSource);
                });
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanUsePayjoin2()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var pjClient = tester.PayTester.GetService<PayjoinClient>();
            var nbx = tester.PayTester.GetService<ExplorerClientProvider>().GetExplorerClient("BTC");
            var notifications = await nbx.CreateWebsocketNotificationSessionAsync();
            var alice = tester.NewAccount();
            await alice.RegisterDerivationSchemeAsync("BTC", ScriptPubKeyType.Segwit, true);

            BitcoinAddress address = null;
            for (int i = 0; i < 5; i++)
            {
                address = (await nbx.GetUnusedAsync(alice.DerivationScheme, DerivationFeature.Deposit)).Address;
                await tester.ExplorerNode.GenerateAsync(1);
                tester.ExplorerNode.SendToAddress(address, Money.Coins(1.0m));
                await notifications.WaitReceive(alice.DerivationScheme);
            }
            var paymentAddress = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.RegTest);
            var otherAddress = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.RegTest);
            var psbt = (await nbx.CreatePSBTAsync(alice.DerivationScheme, new CreatePSBTRequest()
            {
                Destinations =
                    {
                        new CreatePSBTDestination()
                        {
                            Amount = Money.Coins(0.5m),
                            Destination = paymentAddress
                        },
                        new CreatePSBTDestination()
                        {
                            Amount = Money.Coins(0.1m),
                            Destination = otherAddress
                        }
                    },
                FeePreference = new FeePreference()
                {
                    ExplicitFee = Money.Satoshis(3000)
                }
            })).PSBT;
            int paymentIndex = 0;
            int changeIndex = 0;
            int otherIndex = 0;
            for (int i = 0; i < psbt.Outputs.Count; i++)
            {
                if (psbt.Outputs[i].Value == Money.Coins(0.5m))
                    paymentIndex = i;
                else if (psbt.Outputs[i].Value == Money.Coins(0.1m))
                    otherIndex = i;
                else
                    changeIndex = i;
            }

            var derivationSchemeSettings = alice.GetController<UIWalletsController>().GetDerivationSchemeSettings(new WalletId(alice.StoreId, "BTC"));
            var signingAccount = derivationSchemeSettings.GetSigningAccountKeySettings();
            psbt.SignAll(derivationSchemeSettings.AccountDerivation, alice.GenerateWalletResponseV.AccountHDKey, signingAccount.GetRootedKeyPath());
            using var fakeServer = new FakeServer();
            await fakeServer.Start();
            var bip21 = new BitcoinUrlBuilder($"bitcoin:{paymentAddress}?pj={fakeServer.ServerUri}", Network.RegTest);
            var requesting = pjClient.RequestPayjoin(bip21, new PayjoinWallet(derivationSchemeSettings), psbt, default);
            var request = await fakeServer.GetNextRequest();
            Assert.Equal("1", request.Request.Query["v"][0]);
            Assert.Equal(changeIndex.ToString(), request.Request.Query["additionalfeeoutputindex"][0]);
            Assert.Equal("1853", request.Request.Query["maxadditionalfeecontribution"][0]);

            TestLogs.LogInformation("The payjoin receiver tries to make us pay lots of fee");
            var originalPSBT = await ParsePSBT(request);
            var proposalTx = originalPSBT.GetGlobalTransaction();
            proposalTx.Outputs[changeIndex].Value -= Money.Satoshis(1854);
            await request.Response.WriteAsync(PSBT.FromTransaction(proposalTx, Network.RegTest).ToBase64(), Encoding.UTF8);
            fakeServer.Done();
            var ex = await Assert.ThrowsAsync<PayjoinSenderException>(async () => await requesting);
            Assert.Contains("contribution is more than maxadditionalfeecontribution", ex.Message);

            TestLogs.LogInformation("The payjoin receiver tries to change one of our output");
            requesting = pjClient.RequestPayjoin(bip21, new PayjoinWallet(derivationSchemeSettings), psbt, default);
            request = await fakeServer.GetNextRequest();
            originalPSBT = await ParsePSBT(request);
            proposalTx = originalPSBT.GetGlobalTransaction();
            proposalTx.Outputs[otherIndex].Value -= Money.Satoshis(1);
            await request.Response.WriteAsync(PSBT.FromTransaction(proposalTx, Network.RegTest).ToBase64(), Encoding.UTF8);
            fakeServer.Done();
            ex = await Assert.ThrowsAsync<PayjoinSenderException>(async () => await requesting);
            Assert.Contains("The receiver decreased the value of one", ex.Message);
            TestLogs.LogInformation("The payjoin receiver tries to pocket the fee");
            requesting = pjClient.RequestPayjoin(bip21, new PayjoinWallet(derivationSchemeSettings), psbt, default);
            request = await fakeServer.GetNextRequest();
            originalPSBT = await ParsePSBT(request);
            proposalTx = originalPSBT.GetGlobalTransaction();
            proposalTx.Outputs[paymentIndex].Value += Money.Satoshis(1);
            await request.Response.WriteAsync(PSBT.FromTransaction(proposalTx, Network.RegTest).ToBase64(), Encoding.UTF8);
            fakeServer.Done();
            ex = await Assert.ThrowsAsync<PayjoinSenderException>(async () => await requesting);
            Assert.Contains("The receiver decreased absolute fee", ex.Message);

            TestLogs.LogInformation("The payjoin receiver tries to remove one of our output");
            requesting = pjClient.RequestPayjoin(bip21, new PayjoinWallet(derivationSchemeSettings), psbt, default);
            request = await fakeServer.GetNextRequest();
            originalPSBT = await ParsePSBT(request);
            proposalTx = originalPSBT.GetGlobalTransaction();
            var removedOutput = proposalTx.Outputs.First(o => o.ScriptPubKey == otherAddress.ScriptPubKey);
            proposalTx.Outputs.Remove(removedOutput);
            await request.Response.WriteAsync(PSBT.FromTransaction(proposalTx, Network.RegTest).ToBase64(), Encoding.UTF8);
            fakeServer.Done();
            ex = await Assert.ThrowsAsync<PayjoinSenderException>(async () => await requesting);
            Assert.Contains("Some of our outputs are not included in the proposal", ex.Message);

            TestLogs.LogInformation("The payjoin receiver tries to change their own output");
            requesting = pjClient.RequestPayjoin(bip21, new PayjoinWallet(derivationSchemeSettings), psbt, default);
            request = await fakeServer.GetNextRequest();
            originalPSBT = await ParsePSBT(request);
            proposalTx = originalPSBT.GetGlobalTransaction();
            proposalTx.Outputs.First(o => o.ScriptPubKey == paymentAddress.ScriptPubKey).Value -= Money.Satoshis(1);
            await request.Response.WriteAsync(PSBT.FromTransaction(proposalTx, Network.RegTest).ToBase64(), Encoding.UTF8);
            fakeServer.Done();
            await requesting;


            TestLogs.LogInformation("The payjoin receiver tries to send money to himself");
            pjClient.MaxFeeBumpContribution = Money.Satoshis(1);
            requesting = pjClient.RequestPayjoin(bip21, new PayjoinWallet(derivationSchemeSettings), psbt, default);
            request = await fakeServer.GetNextRequest();
            originalPSBT = await ParsePSBT(request);
            proposalTx = originalPSBT.GetGlobalTransaction();
            proposalTx.Outputs[paymentIndex].Value += Money.Satoshis(1);
            proposalTx.Outputs[changeIndex].Value -= Money.Satoshis(1);
            await request.Response.WriteAsync(PSBT.FromTransaction(proposalTx, Network.RegTest).ToBase64(), Encoding.UTF8);
            fakeServer.Done();
            ex = await Assert.ThrowsAsync<PayjoinSenderException>(async () => await requesting);
            Assert.Contains("is not only paying fee", ex.Message);
            pjClient.MaxFeeBumpContribution = null;
            TestLogs.LogInformation("The payjoin receiver can't use additional fee without adding inputs");
            pjClient.MinimumFeeRate = new FeeRate(50m);
            requesting = pjClient.RequestPayjoin(bip21, new PayjoinWallet(derivationSchemeSettings), psbt, default);
            request = await fakeServer.GetNextRequest();
            originalPSBT = await ParsePSBT(request);
            proposalTx = originalPSBT.GetGlobalTransaction();
            proposalTx.Outputs[changeIndex].Value -= Money.Satoshis(1146);
            await request.Response.WriteAsync(PSBT.FromTransaction(proposalTx, Network.RegTest).ToBase64(), Encoding.UTF8);
            fakeServer.Done();
            ex = await Assert.ThrowsAsync<PayjoinSenderException>(async () => await requesting);
            Assert.Contains("is not only paying for additional inputs", ex.Message);
            pjClient.MinimumFeeRate = null;

            TestLogs.LogInformation("Make sure the receiver implementation do not take more fee than allowed");
            var bob = tester.NewAccount();
            await bob.GrantAccessAsync();
            await bob.RegisterDerivationSchemeAsync("BTC", ScriptPubKeyType.Segwit, true);

            await notifications.DisposeAsync();
            notifications = await nbx.CreateWebsocketNotificationSessionAsync();
            address = (await nbx.GetUnusedAsync(bob.DerivationScheme, DerivationFeature.Deposit)).Address;
            tester.ExplorerNode.SendToAddress(address, Money.Coins(1.1m));
            await notifications.WaitReceive(bob.DerivationScheme);
            await bob.ModifyOnchainPaymentSettings(p => p.PayJoinEnabled = true);
            var invoice = bob.BitPay.CreateInvoice(
                new Invoice { Price = 0.1m, Currency = "BTC", FullNotifications = true });
            var invoiceBIP21 = new BitcoinUrlBuilder(invoice.CryptoInfo.First().PaymentUrls.BIP21,
                tester.ExplorerClient.Network.NBitcoinNetwork);

            psbt = (await nbx.CreatePSBTAsync(alice.DerivationScheme, new CreatePSBTRequest()
            {
                Destinations =
                    {
                        new CreatePSBTDestination()
                        {
                            Amount = invoiceBIP21.Amount,
                            Destination = invoiceBIP21.Address
                        }
                    },
                FeePreference = new FeePreference()
                {
                    ExplicitFee = Money.Satoshis(3001)
                }
            })).PSBT;
            psbt.SignAll(derivationSchemeSettings.AccountDerivation, alice.GenerateWalletResponseV.AccountHDKey, signingAccount.GetRootedKeyPath());
            var endpoint = TestAccount.GetPayjoinBitcoinUrl(invoice, Network.RegTest);
            pjClient.MaxFeeBumpContribution = Money.Satoshis(50);
            var proposal = await pjClient.RequestPayjoin(endpoint, new PayjoinWallet(derivationSchemeSettings), psbt, default);
            Assert.True(proposal.TryGetFee(out var newFee));
            Assert.Equal(Money.Satoshis(3001 + 50), newFee);
            proposal = proposal.SignAll(derivationSchemeSettings.AccountDerivation, alice.GenerateWalletResponseV.AccountHDKey, signingAccount.GetRootedKeyPath());
            proposal.Finalize();
            await tester.ExplorerNode.SendRawTransactionAsync(proposal.ExtractTransaction());
            await notifications.WaitReceive(bob.DerivationScheme);

            TestLogs.LogInformation("Abusing minFeeRate should give not enough money error");
            invoice = bob.BitPay.CreateInvoice(
                new Invoice() { Price = 0.1m, Currency = "BTC", FullNotifications = true });
            invoiceBIP21 = new BitcoinUrlBuilder(invoice.CryptoInfo.First().PaymentUrls.BIP21,
                tester.ExplorerClient.Network.NBitcoinNetwork);
            psbt = (await nbx.CreatePSBTAsync(alice.DerivationScheme, new CreatePSBTRequest()
            {
                Destinations =
                    {
                        new CreatePSBTDestination()
                        {
                            Amount = invoiceBIP21.Amount,
                            Destination = invoiceBIP21.Address
                        }
                    },
                FeePreference = new FeePreference()
                {
                    ExplicitFee = Money.Satoshis(3001)
                }
            })).PSBT;
            psbt.SignAll(derivationSchemeSettings.AccountDerivation, alice.GenerateWalletResponseV.AccountHDKey, signingAccount.GetRootedKeyPath());
            endpoint = TestAccount.GetPayjoinBitcoinUrl(invoice, Network.RegTest);
            pjClient.MinimumFeeRate = new FeeRate(100_000_000.2m);
            var ex2 = await Assert.ThrowsAsync<PayjoinReceiverException>(async () => await pjClient.RequestPayjoin(endpoint, new PayjoinWallet(derivationSchemeSettings), psbt, default));
            Assert.Equal(PayjoinReceiverWellknownErrors.NotEnoughMoney, ex2.WellknownError);
        }

        private static async Task<PSBT> ParsePSBT(Microsoft.AspNetCore.Http.HttpContext request)
        {
            var bytes = await request.Request.Body.ReadBytesAsync(int.Parse(request.Request.Headers["Content-Length"].First()));
            var str = Encoding.UTF8.GetString(bytes);
            return PSBT.Parse(str, Network.RegTest);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanUsePayjoinFeeCornerCase()
        {
            using (var tester = CreateServerTester())
            {
                await tester.StartAsync();
                var broadcaster = tester.PayTester.GetService<DelayedTransactionBroadcaster>();
                var payjoinRepository = tester.PayTester.GetService<UTXOLocker>();
                broadcaster.Disable();
                var network = tester.NetworkProvider.GetNetwork<BTCPayNetwork>("BTC");
                var btcPayWallet = tester.PayTester.GetService<BTCPayWalletProvider>().GetWallet(network);
                var cashCow = tester.ExplorerNode;
                cashCow.Generate(2); // get some money in case

                var senderUser = tester.NewAccount();
                senderUser.GrantAccess(true);
                senderUser.RegisterDerivationScheme("BTC", ScriptPubKeyType.Segwit);

                var receiverUser = tester.NewAccount();
                receiverUser.GrantAccess(true);
                receiverUser.RegisterDerivationScheme("BTC", ScriptPubKeyType.Segwit, true);
                await receiverUser.ModifyOnchainPaymentSettings(p => p.PayJoinEnabled = true);
                var receiverCoin = await receiverUser.ReceiveUTXO(Money.Satoshis(810), network);
                string lastInvoiceId = null;

                var vector = (SpentCoin: Money.Satoshis(810), InvoiceAmount: Money.Satoshis(700), Paid: Money.Satoshis(700), Fee: Money.Satoshis(110), InvoicePaid: true, ExpectedError: "not-enough-money", OriginalTxBroadcasted: true);
                async Task<PSBT> RunVector(bool skipLockedCheck = false)
                {
                    var coin = await senderUser.ReceiveUTXO(vector.SpentCoin, network);
                    var invoice = receiverUser.BitPay.CreateInvoice(new Invoice() { Price = vector.InvoiceAmount.ToDecimal(MoneyUnit.BTC), Currency = "BTC", FullNotifications = true });
                    lastInvoiceId = invoice.Id;
                    var invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network);
                    var txBuilder = network.NBitcoinNetwork.CreateTransactionBuilder();
                    txBuilder.OptInRBF = true;
                    txBuilder.AddCoins(coin);
                    txBuilder.Send(invoiceAddress, vector.Paid);
                    txBuilder.SendFees(vector.Fee);
                    txBuilder.SetChange(await senderUser.GetNewAddress(network));
                    var psbt = txBuilder.BuildPSBT(false);
                    psbt = await senderUser.Sign(psbt);
                    var pj = await senderUser.SubmitPayjoin(invoice, psbt, vector.ExpectedError);
                    if (vector.ExpectedError is null)
                    {
                        Assert.Contains(pj.Inputs, o => o.PrevOut == receiverCoin.Outpoint);
                        foreach (var input in pj.GetGlobalTransaction().Inputs)
                        {
                            Assert.Equal(Sequence.OptInRBF, input.Sequence);
                        }
                        if (!skipLockedCheck)
                            Assert.True(await payjoinRepository.TryUnlock(receiverCoin.Outpoint));
                    }
                    else
                    {
                        Assert.Null(pj);
                        if (!skipLockedCheck)
                            Assert.False(await payjoinRepository.TryUnlock(receiverCoin.Outpoint));
                    }

                    if (vector.InvoicePaid)
                    {
                        await TestUtils.EventuallyAsync(async () =>
                        {
                            invoice = await receiverUser.BitPay.GetInvoiceAsync(invoice.Id);
                            Assert.Equal("paid", invoice.Status);
                        });
                    }

                    psbt.Finalize();
                    var broadcasted = await tester.PayTester.GetService<ExplorerClientProvider>().GetExplorerClient("BTC").BroadcastAsync(psbt.ExtractTransaction(), true);
                    if (vector.OriginalTxBroadcasted)
                    {
                        Assert.Equal("txn-already-in-mempool", broadcasted.RPCCodeMessage);
                    }
                    else
                    {
                        Assert.True(broadcasted.Success);
                    }
                    receiverCoin = await receiverUser.ReceiveUTXO(receiverCoin.Amount, network);
                    await LockAllButReceiverCoin();
                    return pj;
                }

                async Task LockAllButReceiverCoin()
                {
                    var coins = await btcPayWallet.GetUnspentCoins(receiverUser.DerivationScheme);
                    foreach (var coin in coins)
                    {
                        if (coin.OutPoint != receiverCoin.Outpoint)
                            await payjoinRepository.TryLock(coin.OutPoint);
                        else
                            await payjoinRepository.TryUnlock(coin.OutPoint);
                    }
                }

                TestLogs.LogInformation("Here we send exactly the right amount. This should fails as\n" +
                                           "there is not enough to pay the additional payjoin input. (going below the min relay fee" +
                                           "However, the original tx has been broadcasted!");
                vector = (SpentCoin: Money.Satoshis(810), InvoiceAmount: Money.Satoshis(700), Paid: Money.Satoshis(700), Fee: Money.Satoshis(110), InvoicePaid: true, ExpectedError: "not-enough-money", OriginalTxBroadcasted: true);
                await RunVector();

                TestLogs.LogInformation("We don't pay enough");
                vector = (SpentCoin: Money.Satoshis(810), InvoiceAmount: Money.Satoshis(700), Paid: Money.Satoshis(690), Fee: Money.Satoshis(110), InvoicePaid: false, ExpectedError: "invoice-not-fully-paid", OriginalTxBroadcasted: true);
                await RunVector();

                TestLogs.LogInformation("We pay correctly");
                vector = (SpentCoin: Money.Satoshis(810), InvoiceAmount: Money.Satoshis(500), Paid: Money.Satoshis(500), Fee: Money.Satoshis(110), InvoicePaid: true, ExpectedError: null as string, OriginalTxBroadcasted: false);
                await RunVector();

                PSBT proposedPSBT = null;
                var outputCountReceived = new bool[2];
                do
                {
                    TestLogs.LogInformation("We pay a little bit more the invoice with enough fees to support additional input\n" +
                                               "The receiver should have added a fake output");
                    vector = (SpentCoin: Money.Satoshis(910), InvoiceAmount: Money.Satoshis(500), Paid: Money.Satoshis(700), Fee: Money.Satoshis(110), InvoicePaid: true, ExpectedError: null as string, OriginalTxBroadcasted: false);
                    proposedPSBT = await RunVector();
                    Assert.True(proposedPSBT.Outputs.Count == 1 || proposedPSBT.Outputs.Count == 2);
                    outputCountReceived[proposedPSBT.Outputs.Count - 1] = true;
                    cashCow.Generate(1);
                } while (outputCountReceived.All(o => o));

                TestLogs.LogInformation("We pay correctly, but no utxo\n" +
                                           "However, this has the side effect of having the receiver broadcasting the original tx");
                await payjoinRepository.TryLock(receiverCoin.Outpoint);
                vector = (SpentCoin: Money.Satoshis(810), InvoiceAmount: Money.Satoshis(500), Paid: Money.Satoshis(500), Fee: Money.Satoshis(110), InvoicePaid: true, ExpectedError: "unavailable|any UTXO available", OriginalTxBroadcasted: true);
                await RunVector(true);

                var originalSenderUser = senderUser;
retry:
// Additional fee is 96 , minrelaytx is 294
// We pay correctly, fees partially taken from what is overpaid
// We paid 510, the receiver pay 10 sat
// The send pay remaining 86 sat from his pocket
// So total paid by sender should be 86 + 510 + 200 so we should get 1090 - (86 + 510 + 200) == 294 back)
                TestLogs.LogInformation($"Check if we can take fee on overpaid utxo{(senderUser == receiverUser ? " (to self)" : "")}");
                vector = (SpentCoin: Money.Satoshis(1090), InvoiceAmount: Money.Satoshis(500), Paid: Money.Satoshis(510), Fee: Money.Satoshis(200), InvoicePaid: true, ExpectedError: null as string, OriginalTxBroadcasted: false);
                proposedPSBT = await RunVector();
                Assert.Equal(2, proposedPSBT.Outputs.Count);
                Assert.Contains(proposedPSBT.Outputs, o => o.Value == Money.Satoshis(500) + receiverCoin.Amount);
                Assert.Contains(proposedPSBT.Outputs, o => o.Value == Money.Satoshis(294));
                proposedPSBT = await senderUser.Sign(proposedPSBT);
                proposedPSBT = proposedPSBT.Finalize();
                var explorerClient = tester.PayTester.GetService<ExplorerClientProvider>().GetExplorerClient(proposedPSBT.Network.NetworkSet.CryptoCode);
                var result = await explorerClient.BroadcastAsync(proposedPSBT.ExtractTransaction());
                Assert.True(result.Success);
                TestLogs.LogInformation($"We broadcasted the payjoin {proposedPSBT.ExtractTransaction().GetHash()}");
                TestLogs.LogInformation($"Let's make sure that the coinjoin is not over paying, since the 10 overpaid sats have gone to fee");
                await TestUtils.EventuallyAsync(async () =>
                {
                    var invoice = await tester.PayTester.GetService<InvoiceRepository>().GetInvoice(lastInvoiceId);
                    Assert.Equal(InvoiceStatus.Processing, invoice.Status);
                    Assert.Equal(InvoiceExceptionStatus.None, invoice.ExceptionStatus);
                    var coins = await btcPayWallet.GetUnspentCoins(receiverUser.DerivationScheme);
                    foreach (var coin in coins)
                        await payjoinRepository.TryLock(coin.OutPoint);
                });
                tester.ExplorerNode.Generate(1);
                receiverCoin = await receiverUser.ReceiveUTXO(Money.Satoshis(810), network);
                await LockAllButReceiverCoin();
                if (senderUser != receiverUser)
                {
                    TestLogs.LogInformation("Let's do the same, this time paying to ourselves");
                    senderUser = receiverUser;
                    goto retry;
                }
                else
                {
                    senderUser = originalSenderUser;
                }


                // Same as above. Except the sender send one satoshi less, so the change
                // output would get below dust and would be removed completely.
                // So we remove as much fee as we can, and still accept the transaction because it is above minrelay fee
                vector = (SpentCoin: Money.Satoshis(1089), InvoiceAmount: Money.Satoshis(500), Paid: Money.Satoshis(510), Fee: Money.Satoshis(200), InvoicePaid: true, ExpectedError: null as string, OriginalTxBroadcasted: false);
                proposedPSBT = await RunVector();
                Assert.Equal(2, proposedPSBT.Outputs.Count);
                // We should have our payment
                Assert.Contains(proposedPSBT.Outputs, output => output.Value == Money.Satoshis(500) + receiverCoin.Amount);
                // Plus our other change output with value just at dust level
                Assert.Contains(proposedPSBT.Outputs, output => output.Value == Money.Satoshis(294));
                proposedPSBT = await senderUser.Sign(proposedPSBT);
                proposedPSBT = proposedPSBT.Finalize();
                explorerClient = tester.PayTester.GetService<ExplorerClientProvider>().GetExplorerClient(proposedPSBT.Network.NetworkSet.CryptoCode);
                result = await explorerClient.BroadcastAsync(proposedPSBT.ExtractTransaction(), true);
                Assert.True(result.Success);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUsePayjoin()
        {
            using (var tester = CreateServerTester())
            {
                await tester.StartAsync();

                ////var payJoinStateProvider = tester.PayTester.GetService<PayJoinStateProvider>();
                var btcPayNetwork = tester.NetworkProvider.GetNetwork<BTCPayNetwork>("BTC");
                var btcPayWallet = tester.PayTester.GetService<BTCPayWalletProvider>().GetWallet(btcPayNetwork);
                var cashCow = tester.ExplorerNode;
                cashCow.Generate(2); // get some money in case

                var senderUser = tester.NewAccount();
                senderUser.GrantAccess(true);
                senderUser.RegisterDerivationScheme("BTC", ScriptPubKeyType.Segwit, true);

                var invoice = senderUser.BitPay.CreateInvoice(
                    new Invoice() { Price = 100, Currency = "USD", FullNotifications = true });
                //payjoin is not enabled by default.
                Assert.DoesNotContain($"{PayjoinClient.BIP21EndpointKey}=", invoice.CryptoInfo.First().PaymentUrls.BIP21);
                cashCow.SendToAddress(BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network),
                    Money.Coins(0.06m));

                var receiverUser = tester.NewAccount();
                receiverUser.GrantAccess(true);
                receiverUser.RegisterDerivationScheme("BTC", ScriptPubKeyType.Segwit, true);

                await receiverUser.ModifyOnchainPaymentSettings(p => p.PayJoinEnabled = true);
                // payjoin is enabled, with a segwit wallet, and the keys are available in nbxplorer
                invoice = receiverUser.BitPay.CreateInvoice(
                    new Invoice() { Price = 0.02m, Currency = "BTC", FullNotifications = true });
                cashCow.SendToAddress(BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network),
                    Money.Coins(0.06m));

                //give the cow some cash
                await cashCow.GenerateAsync(1);
                //let's get some more utxos first
                foreach (var m in new[]
                {
                    Money.Coins(0.011m),
                    Money.Coins(0.012m),
                    Money.Coins(0.013m),
                    Money.Coins(0.014m),
                    Money.Coins(0.015m),
                    Money.Coins(0.016m)
                })
                {
                    await receiverUser.ReceiveUTXO(m, btcPayNetwork);
                }

                foreach (var m in new[]
                {
                    Money.Coins(0.021m),
                    Money.Coins(0.022m),
                    Money.Coins(0.023m),
                    Money.Coins(0.024m),
                    Money.Coins(0.025m),
                    Money.Coins(0.026m)
                })
                {
                    await senderUser.ReceiveUTXO(m, btcPayNetwork);
                }

                var senderChange = await senderUser.GetNewAddress(btcPayNetwork);

                //Let's start the harassment
                invoice = receiverUser.BitPay.CreateInvoice(
                    new Invoice() { Price = 0.02m, Currency = "BTC", FullNotifications = true });
                // Bad version should throw incorrect version
                var bip21 = TestAccount.GetPayjoinBitcoinUrl(invoice, btcPayNetwork.NBitcoinNetwork);
                bip21.TryGetPayjoinEndpoint(out var endpoint);
                var response = await tester.PayTester.HttpClient.PostAsync(endpoint.AbsoluteUri + "?v=2",
                    new StringContent("", Encoding.UTF8, "text/plain"));
                Assert.False(response.IsSuccessStatusCode);
                var error = JObject.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal("version-unsupported", error["errorCode"].Value<string>());
                Assert.Equal(1, ((JArray)error["supported"]).Single().Value<int>());

                var parsedBip21 = new BitcoinUrlBuilder(invoice.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);

                var invoice2 = receiverUser.BitPay.CreateInvoice(
                    new Invoice() { Price = 0.02m, Currency = "BTC", FullNotifications = true });
                var secondInvoiceParsedBip21 = new BitcoinUrlBuilder(invoice2.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);

                var senderStore = await tester.PayTester.StoreRepository.FindStore(senderUser.StoreId);
                var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId("BTC");
                var handlers = tester.PayTester.GetService<PaymentMethodHandlerDictionary>();
                var derivationSchemeSettings = senderStore.GetPaymentMethodConfig<DerivationSchemeSettings>(paymentMethodId, handlers);

                ReceivedCoin[] senderCoins = null;
                ReceivedCoin coin = null;
                ReceivedCoin coin2 = null;
                ReceivedCoin coin3 = null;
                ReceivedCoin coin4 = null;
                ReceivedCoin coin5 = null;
                ReceivedCoin coin6 = null;
                await TestUtils.EventuallyAsync(async () =>
                {
                    senderCoins = await btcPayWallet.GetUnspentCoins(senderUser.DerivationScheme);
                    Assert.Contains(senderCoins, coin => coin.Value.GetValue(btcPayNetwork) == 0.026m);
                    coin = Assert.Single(senderCoins, coin => coin.Value.GetValue(btcPayNetwork) == 0.021m);
                    coin2 = Assert.Single(senderCoins, coin => coin.Value.GetValue(btcPayNetwork) == 0.022m);
                    coin3 = Assert.Single(senderCoins, coin => coin.Value.GetValue(btcPayNetwork) == 0.023m);
                    coin4 = Assert.Single(senderCoins, coin => coin.Value.GetValue(btcPayNetwork) == 0.024m);
                    coin5 = Assert.Single(senderCoins, coin => coin.Value.GetValue(btcPayNetwork) == 0.025m);
                    coin6 = Assert.Single(senderCoins, coin => coin.Value.GetValue(btcPayNetwork) == 0.026m);
                });


                var signingKeySettings = derivationSchemeSettings.GetSigningAccountKeySettings();
                signingKeySettings.RootFingerprint =
                    senderUser.GenerateWalletResponseV.MasterHDKey.GetPublicKey().GetHDFingerPrint();

                var extKey =
                    senderUser.GenerateWalletResponseV.MasterHDKey.Derive(signingKeySettings.GetRootedKeyPath()
                        .KeyPath);

                var Invoice1Coin1 = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
                    .SetChange(senderChange)
                    .Send(parsedBip21.Address, parsedBip21.Amount)
                    .AddCoins(coin.Coin)
                    .AddKeys(extKey.Derive(coin.KeyPath))
                    .SendEstimatedFees(new FeeRate(100m))
                    .BuildTransaction(true);

                tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
                    .SetChange(senderChange)
                    .Send(parsedBip21.Address, parsedBip21.Amount)
                    .AddCoins(coin2.Coin)
                    .AddKeys(extKey.Derive(coin2.KeyPath))
                    .SendEstimatedFees(new FeeRate(100m))
                    .BuildTransaction(true);

                var Invoice2Coin1 = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
                    .SetChange(senderChange)
                    .Send(secondInvoiceParsedBip21.Address, secondInvoiceParsedBip21.Amount)
                    .AddCoins(coin.Coin)
                    .AddKeys(extKey.Derive(coin.KeyPath))
                    .SendEstimatedFees(new FeeRate(100m))
                    .BuildTransaction(true);

                var Invoice2Coin2 = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
                    .SetChange(senderChange)
                    .Send(secondInvoiceParsedBip21.Address, secondInvoiceParsedBip21.Amount)
                    .AddCoins(coin2.Coin)
                    .AddKeys(extKey.Derive(coin2.KeyPath))
                    .SendEstimatedFees(new FeeRate(100m))
                    .BuildTransaction(true);


                //Attempt 2: Create two transactions using different inputs and send them to the same invoice. 
                //Result: Second Tx should be rejected. 
                var Invoice1Coin1ResponseTx = await senderUser.SubmitPayjoin(invoice, Invoice1Coin1, btcPayNetwork);

                await senderUser.SubmitPayjoin(invoice, Invoice1Coin1, btcPayNetwork, "already-paid");
                var contributedInputsInvoice1Coin1ResponseTx =
                    Invoice1Coin1ResponseTx.Inputs.Where(txin => coin.OutPoint != txin.PrevOut);
                Assert.Single(contributedInputsInvoice1Coin1ResponseTx);

                //Attempt 3: Send the same inputs from invoice 1 to invoice 2 while invoice 1 tx has not been broadcasted
                //Result: Reject Tx1 but accept tx 2 as its inputs were never accepted by invoice 1
                await senderUser.SubmitPayjoin(invoice2, Invoice2Coin1, btcPayNetwork, expectedError: "unavailable|Some of those inputs have already been used");
                var Invoice2Coin2ResponseTx = await senderUser.SubmitPayjoin(invoice2, Invoice2Coin2, btcPayNetwork);

                var contributedInputsInvoice2Coin2ResponseTx =
                    Invoice2Coin2ResponseTx.Inputs.Where(txin => coin2.OutPoint != txin.PrevOut);
                Assert.Single(contributedInputsInvoice2Coin2ResponseTx);

                //Attempt 4: Make tx that pays invoice 3 and 4 and submit to both
                //Result: reject on 4: the protocol should not worry about this complexity

                var invoice3 = receiverUser.BitPay.CreateInvoice(
                    new Invoice() { Price = 0.01m, Currency = "BTC", FullNotifications = true });
                var invoice3ParsedBip21 = new BitcoinUrlBuilder(invoice3.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);


                var invoice4 = receiverUser.BitPay.CreateInvoice(
                    new Invoice() { Price = 0.01m, Currency = "BTC", FullNotifications = true });
                var invoice4ParsedBip21 = new BitcoinUrlBuilder(invoice4.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);


                var Invoice3AndInvoice4Coin3 = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
                    .SetChange(senderChange)
                    .Send(invoice3ParsedBip21.Address, invoice3ParsedBip21.Amount)
                    .Send(invoice4ParsedBip21.Address, invoice4ParsedBip21.Amount)
                    .AddCoins(coin3.Coin)
                    .AddKeys(extKey.Derive(coin3.KeyPath))
                    .SendEstimatedFees(new FeeRate(100m))
                    .BuildTransaction(true);

                await senderUser.SubmitPayjoin(invoice3, Invoice3AndInvoice4Coin3, btcPayNetwork);
                await senderUser.SubmitPayjoin(invoice4, Invoice3AndInvoice4Coin3, btcPayNetwork, "already-paid");

                //Attempt 5: Make tx that pays invoice 5 with 2 outputs
                //Result: proposed tx consolidates the outputs

                var invoice5 = receiverUser.BitPay.CreateInvoice(
                    new Invoice() { Price = 0.01m, Currency = "BTC", FullNotifications = true });
                var invoice5ParsedBip21 = new BitcoinUrlBuilder(invoice5.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);

                var Invoice5Coin4TxBuilder = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
                    .SetChange(senderChange)
                    .Send(invoice5ParsedBip21.Address, invoice5ParsedBip21.Amount / 2)
                    .Send(invoice5ParsedBip21.Address, invoice5ParsedBip21.Amount / 2)
                    .AddCoins(coin4.Coin)
                    .AddKeys(extKey.Derive(coin4.KeyPath))
                    .SendEstimatedFees(new FeeRate(100m));

                var Invoice5Coin4 = Invoice5Coin4TxBuilder.BuildTransaction(true);
                var Invoice5Coin4ResponseTx = await senderUser.SubmitPayjoin(invoice5, Invoice5Coin4, btcPayNetwork);
                Assert.Single(Invoice5Coin4ResponseTx.Outputs.To(invoice5ParsedBip21.Address));

                //Attempt 10: send tx with rbf, broadcast payjoin tx, bump the rbf payjoin , attempt to submit tx again
                //Result: same tx gets sent back

                //give the receiver some more utxos
                Assert.NotNull(await tester.ExplorerNode.SendToAddressAsync(
                    (await btcPayWallet.ReserveAddressAsync(receiverUser.DerivationScheme)).Address,
                    new Money(0.1m, MoneyUnit.BTC)));

                var invoice6 = receiverUser.BitPay.CreateInvoice(
                    new Invoice() { Price = 0.01m, Currency = "BTC", FullNotifications = true });
                var invoice6ParsedBip21 = new BitcoinUrlBuilder(invoice6.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);

                var invoice6Coin5TxBuilder = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
                    .SetChange(senderChange)
                    .Send(invoice6ParsedBip21.Address, invoice6ParsedBip21.Amount)
                    .AddCoins(coin5.Coin)
                    .AddKeys(extKey.Derive(coin5.KeyPath))
                    .SendEstimatedFees(new FeeRate(100m))
                    .SetLockTime(0);

                var invoice6Coin5 = invoice6Coin5TxBuilder
                    .BuildTransaction(true);

                var Invoice6Coin5Response1Tx = await senderUser.SubmitPayjoin(invoice6, invoice6Coin5, btcPayNetwork);
                var Invoice6Coin5Response1TxSigned = invoice6Coin5TxBuilder.SignTransaction(Invoice6Coin5Response1Tx);
                //broadcast the first payjoin
                await tester.ExplorerClient.BroadcastAsync(Invoice6Coin5Response1TxSigned);

                // invoice6Coin5TxBuilder = invoice6Coin5TxBuilder.SendEstimatedFees(new FeeRate(100m));
                // var invoice6Coin5Bumpedfee = invoice6Coin5TxBuilder
                //     .BuildTransaction(true);
                //
                // var Invoice6Coin5Response3 = await tester.PayTester.HttpClient.PostAsync(invoice6Endpoint,
                //     new StringContent(invoice6Coin5Bumpedfee.ToHex(), Encoding.UTF8, "text/plain"));
                // Assert.True(Invoice6Coin5Response3.IsSuccessStatusCode);
                // var Invoice6Coin5Response3Tx =
                //     Transaction.Parse(await Invoice6Coin5Response3.Content.ReadAsStringAsync(), n);
                // Assert.True(invoice6Coin5Bumpedfee.Inputs.All(txin =>
                //     Invoice6Coin5Response3Tx.Inputs.Any(txin2 => txin2.PrevOut == txin.PrevOut)));

                //Attempt 11:
                //send tx with rbt, broadcast payjoin,
                //create tx spending the original tx inputs with rbf to self,
                //Result: the exposed utxos are priorized in the next p2ep

                //give the receiver some more utxos
                Assert.NotNull(await tester.ExplorerNode.SendToAddressAsync(
                    (await btcPayWallet.ReserveAddressAsync(receiverUser.DerivationScheme)).Address,
                    new Money(0.1m, MoneyUnit.BTC)));

                var invoice7 = receiverUser.BitPay.CreateInvoice(
                    new Invoice() { Price = 0.01m, Currency = "BTC", FullNotifications = true });
                var invoice7ParsedBip21 = new BitcoinUrlBuilder(invoice7.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);

                var txBuilder = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder();
                txBuilder.OptInRBF = true;
                var invoice7Coin6TxBuilder = txBuilder
                    .SetChange(senderChange)
                    .Send(invoice7ParsedBip21.Address, invoice7ParsedBip21.Amount)
                    .AddCoins(coin6.Coin)
                    .AddKeys(extKey.Derive(coin6.KeyPath))
                    .SendEstimatedFees(new FeeRate(100m));

                var invoice7Coin6Tx = invoice7Coin6TxBuilder
                    .BuildTransaction(true);

                var invoice7Coin6Response1Tx = await senderUser.SubmitPayjoin(invoice7, invoice7Coin6Tx, btcPayNetwork);
                var Invoice7Coin6Response1TxSigned = invoice7Coin6TxBuilder.SignTransaction(invoice7Coin6Response1Tx);
                Invoice7Coin6Response1TxSigned.Inputs.Single(txin => coin6.OutPoint != txin.PrevOut);


                ////var receiverWalletPayJoinState = payJoinStateProvider.Get(receiverWalletId);
                ////Assert.Contains(receiverWalletPayJoinState.GetRecords(), item => item.InvoiceId == invoice7.Id);
                //broadcast the payjoin
                var res = (await tester.ExplorerClient.BroadcastAsync(Invoice7Coin6Response1TxSigned));
                Assert.True(res.Success);
                var handler = handlers.GetBitcoinHandler("BTC");
                // Paid with coinjoin
                await TestUtils.EventuallyAsync(async () =>
                {
                    var invoiceEntity = await tester.PayTester.GetService<InvoiceRepository>().GetInvoice(invoice7.Id);
                    Assert.Equal(InvoiceStatus.Processing, invoiceEntity.Status);
                    Assert.Contains(invoiceEntity.GetPayments(false), p => p.Accounted &&
                                                                      handler.ParsePaymentDetails(p.Details).PayjoinInformation is null);
                });
                ////Assert.Contains(receiverWalletPayJoinState.GetRecords(), item => item.InvoiceId == invoice7.Id && item.TxSeen);

                var invoice7Coin6Tx2 = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
                    .SetChange(senderChange)
                    .AddCoins(coin6.Coin)
                    .SendAll(senderChange)
                    .SubtractFees()
                    .AddKeys(extKey.Derive(coin6.KeyPath))
                    .SendEstimatedFees(new FeeRate(200m))
                    .SetLockTime(0)
                    .BuildTransaction(true);

                //broadcast the "rbf cancel" tx
                res = (await tester.ExplorerClient.BroadcastAsync(invoice7Coin6Tx2));
                Assert.True(res.Success);

                // Make a block, this should put back the invoice to new
                var blockhash = tester.ExplorerNode.Generate(1)[0];
                Assert.NotNull(await tester.ExplorerNode.GetRawTransactionAsync(invoice7Coin6Tx2.GetHash(), blockhash));
                Assert.Null(await tester.ExplorerNode.GetRawTransactionAsync(Invoice7Coin6Response1TxSigned.GetHash(), blockhash, false));
                // Now we should return to New
                OutPoint ourOutpoint = null;
                await TestUtils.EventuallyAsync(async () =>
                {
                    var invoiceEntity = await tester.PayTester.GetService<InvoiceRepository>().GetInvoice(invoice7.Id);
                    Assert.Equal(InvoiceStatus.New, invoiceEntity.Status);
                    Assert.True(invoiceEntity.GetPayments(false).All(p => !p.Accounted));
                    ourOutpoint = invoiceEntity.GetAllBitcoinPaymentData(handler, false).First().PayjoinInformation.ContributedOutPoints[0];
                });
                var payjoinRepository = tester.PayTester.GetService<UTXOLocker>();
                // The outpoint should now be available for next pj selection
                Assert.False(await payjoinRepository.TryUnlock(ourOutpoint));
            }
        }
    }
}
