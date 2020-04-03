using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.PayJoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.ObjectModel;
using NBitcoin;
using NBitcoin.Payment;
using NBitpayClient;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class PayJoinTests
    {
        public const int TestTimeout = 60_000;

        public PayJoinTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact] 
        [Trait("Selenium", "Selenium")] 
        public async Task CanUseBIP79Client()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                var invoiceRepository = s.Server.PayTester.GetService<InvoiceRepository>();
                // var payjoinRepository = s.Server.PayTester.GetService<PayJoinRepository>();
                // var broadcaster = s.Server.PayTester.GetService<DelayedTransactionBroadcaster>();
                s.RegisterNewUser(true);
                var receiver = s.CreateNewStore();
                var receiverSeed = s.GenerateWallet("BTC", "", true, true);
                var receiverWalletId = new WalletId(receiver.storeId, "BTC");

                //payjoin is not enabled by default.
                var invoiceId = s.CreateInvoice(receiver.storeId);
                s.GoToInvoiceCheckout(invoiceId);
                var bip21 = s.Driver.FindElement(By.ClassName("payment__details__instruction__open-wallet__btn"))
                    .GetAttribute("href");
                Assert.DoesNotContain("bpu=", bip21);
                
                s.GoToHome();
                s.GoToStore(receiver.storeId);
                //payjoin is not enabled by default.
                Assert.False(s.Driver.FindElement(By.Id("PayJoinEnabled")).Selected);
                s.SetCheckbox(s,"PayJoinEnabled", true);
                s.Driver.FindElement(By.Id("Save")).Click();
                Assert.True(s.Driver.FindElement(By.Id("PayJoinEnabled")).Selected);
                var sender = s.CreateNewStore();
                var senderSeed = s.GenerateWallet("BTC", "", true, true);
                var senderWalletId = new WalletId(sender.storeId, "BTC");
                await s.Server.ExplorerNode.GenerateAsync(1);
                await s.FundStoreWallet(senderWalletId);

                invoiceId = s.CreateInvoice(receiver.storeId);
                s.GoToInvoiceCheckout(invoiceId);
                bip21 = s.Driver.FindElement(By.ClassName("payment__details__instruction__open-wallet__btn"))
                    .GetAttribute("href");
                Assert.Contains("bpu=", bip21);

                s.GoToWalletSend(senderWalletId);
                s.Driver.FindElement(By.Id("bip21parse")).Click();
                s.Driver.SwitchTo().Alert().SendKeys(bip21);
                s.Driver.SwitchTo().Alert().Accept();
                Assert.False(string.IsNullOrEmpty(s.Driver.FindElement(By.Id("PayJoinEndpointUrl")).GetAttribute("value")));
                s.Driver.ScrollTo(By.Id("SendMenu"));
                s.Driver.FindElement(By.Id("SendMenu")).ForceClick();
                s.Driver.FindElement(By.CssSelector("button[value=nbx-seed]")).Click();
                await s.Server.WaitForEvent<NewOnChainTransactionEvent>(() =>
                {
                    s.Driver.FindElement(By.CssSelector("button[value=payjoin]")).ForceClick();
                    return Task.CompletedTask;
                });
                //no funds in receiver wallet to do payjoin
                s.AssertHappyMessage(StatusMessageModel.StatusSeverity.Warning);
                await TestUtils.EventuallyAsync(async () =>
                {
                    var invoice = await s.Server.PayTester.GetService<InvoiceRepository>().GetInvoice(invoiceId);
                    Assert.Equal(InvoiceStatus.Paid, invoice.Status);
                });

                s.GoToInvoices();
                var paymentValueRowColumn = s.Driver.FindElement(By.Id($"invoice_{invoiceId}")).FindElement(By.ClassName("payment-value"));
                Assert.False(paymentValueRowColumn.Text.Contains("payjoin", StringComparison.InvariantCultureIgnoreCase));

                //let's do it all again, except now the receiver has funds and is able to payjoin
                invoiceId = s.CreateInvoice(receiver.storeId);
                s.GoToInvoiceCheckout(invoiceId);
                bip21 = s.Driver.FindElement(By.ClassName("payment__details__instruction__open-wallet__btn"))
                    .GetAttribute("href");
                Assert.Contains("bpu", bip21);

                s.GoToWalletSend(senderWalletId);
                s.Driver.FindElement(By.Id("bip21parse")).Click();
                s.Driver.SwitchTo().Alert().SendKeys(bip21);
                s.Driver.SwitchTo().Alert().Accept();
                Assert.False(string.IsNullOrEmpty(s.Driver.FindElement(By.Id("PayJoinEndpointUrl")).GetAttribute("value")));
                s.Driver.ScrollTo(By.Id("SendMenu"));
                s.Driver.FindElement(By.Id("SendMenu")).ForceClick();
                s.Driver.FindElement(By.CssSelector("button[value=nbx-seed]")).Click();
                await s.Server.WaitForEvent<NewOnChainTransactionEvent>(() =>
                {
                    s.Driver.FindElement(By.CssSelector("button[value=payjoin]")).ForceClick();
                    return Task.CompletedTask;
                });
                s.AssertHappyMessage(StatusMessageModel.StatusSeverity.Success);
                await TestUtils.EventuallyAsync(async () =>
                {
                    var invoice = await invoiceRepository.GetInvoice(invoiceId);
                    var payments = invoice.GetPayments().ToArray();
                    var originalPayment = payments
                        .Single(p =>
                            p.GetCryptoPaymentData() is BitcoinLikePaymentData pd &&
                            pd.PayjoinInformation?.Type is PayjoinTransactionType.Original);
                    var coinjoinPayment = payments
                        .Single(p =>
                            p.GetCryptoPaymentData() is BitcoinLikePaymentData pd &&
                            pd.PayjoinInformation?.Type is PayjoinTransactionType.Coinjoin);
                    Assert.Equal(-1, ((BitcoinLikePaymentData)originalPayment.GetCryptoPaymentData()).ConfirmationCount);
                    Assert.Equal(0, ((BitcoinLikePaymentData)coinjoinPayment.GetCryptoPaymentData()).ConfirmationCount);
                    Assert.False(originalPayment.Accounted);
                    Assert.True(coinjoinPayment.Accounted);
                    Assert.Equal(((BitcoinLikePaymentData)originalPayment.GetCryptoPaymentData()).Value,
                        ((BitcoinLikePaymentData)coinjoinPayment.GetCryptoPaymentData()).Value);
                });
                
                await TestUtils.EventuallyAsync(async () =>
                {
                    var invoice = await s.Server.PayTester.GetService<InvoiceRepository>().GetInvoice(invoiceId);
                    var dto = invoice.EntityToDTO();
                    Assert.Equal(InvoiceStatus.Paid, invoice.Status);
                });
                s.GoToInvoices();
                paymentValueRowColumn = s.Driver.FindElement(By.Id($"invoice_{invoiceId}")).FindElement(By.ClassName("payment-value"));
                Assert.False(paymentValueRowColumn.Text.Contains("payjoin", StringComparison.InvariantCultureIgnoreCase));
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanUseBIP79FeeCornerCase()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var broadcaster = tester.PayTester.GetService<DelayedTransactionBroadcaster>();
                var payjoinRepository = tester.PayTester.GetService<PayJoinRepository>();
                broadcaster.Disable();
                var network = tester.NetworkProvider.GetNetwork<BTCPayNetwork>("BTC");
                var btcPayWallet = tester.PayTester.GetService<BTCPayWalletProvider>().GetWallet(network);
                var cashCow = tester.ExplorerNode;
                cashCow.Generate(2); // get some money in case

                var senderUser = tester.NewAccount();
                senderUser.GrantAccess(true);
                senderUser.RegisterDerivationScheme("BTC", true);

                var receiverUser = tester.NewAccount();
                receiverUser.GrantAccess(true);
                receiverUser.RegisterDerivationScheme("BTC", true, true);
                await receiverUser.EnablePayJoin();
                var receiverCoin = await receiverUser.ReceiveUTXO(Money.Satoshis(810), network);
                string lastInvoiceId = null;

                var vector = (SpentCoin: Money.Satoshis(810), InvoiceAmount: Money.Satoshis(700), Paid: Money.Satoshis(700), Fee: Money.Satoshis(110), ExpectLocked: false, ExpectedError: "not-enough-money");
                async Task<PSBT> RunVector()
                {
                    var coin = await senderUser.ReceiveUTXO(vector.SpentCoin, network);
                    var invoice = receiverUser.BitPay.CreateInvoice(new Invoice() {Price = vector.InvoiceAmount.ToDecimal(MoneyUnit.BTC), Currency = "BTC", FullNotifications = true});
                    lastInvoiceId = invoice.Id;
                    var invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network);
                    var txBuilder = network.NBitcoinNetwork.CreateTransactionBuilder();
                    txBuilder.AddCoins(coin);
                    txBuilder.Send(invoiceAddress, vector.Paid);
                    txBuilder.SendFees(vector.Fee);
                    txBuilder.SetChange(await senderUser.GetNewAddress(network));
                    var psbt = txBuilder.BuildPSBT(false);
                    psbt = await senderUser.Sign(psbt);
                    var pj = await senderUser.SubmitPayjoin(invoice, psbt, vector.ExpectedError);
                    if (vector.ExpectLocked)
                    {
                        Assert.True(await payjoinRepository.TryUnlock(receiverCoin.Outpoint));
                    }
                    else
                    {
                        Assert.False(await payjoinRepository.TryUnlock(receiverCoin.Outpoint));
                    }
                    return pj;
                }
                // Here we send exactly the right amount. This should fails as
                // there is not enough to pay the additional payjoin input. (going below the min relay fee)
                vector = (SpentCoin: Money.Satoshis(810), InvoiceAmount: Money.Satoshis(700), Paid: Money.Satoshis(700), Fee: Money.Satoshis(110), ExpectLocked: false, ExpectedError: "not-enough-money");
                await RunVector();

                // We don't pay enough
                vector = (SpentCoin: Money.Satoshis(810), InvoiceAmount: Money.Satoshis(700), Paid: Money.Satoshis(690), Fee: Money.Satoshis(110), ExpectLocked: false, ExpectedError: "invoice-not-fully-paid");
                await RunVector();
                
                // We pay correctly
                vector = (SpentCoin: Money.Satoshis(810), InvoiceAmount: Money.Satoshis(500), Paid: Money.Satoshis(500), Fee: Money.Satoshis(110), ExpectLocked: true, ExpectedError: null as string);
                await RunVector();
                
                // We pay correctly, but no utxo
                // however, this has the side effect of having the receiver broadcasting the original tx
                await payjoinRepository.TryLock(receiverCoin.Outpoint);
                vector = (SpentCoin: Money.Satoshis(810), InvoiceAmount: Money.Satoshis(500), Paid: Money.Satoshis(500), Fee: Money.Satoshis(110), ExpectLocked: true, ExpectedError: "out-of-utxos");
                await RunVector();
                await TestUtils.EventuallyAsync(async () =>
                {
                    var coins = await btcPayWallet.GetUnspentCoins(receiverUser.DerivationScheme);
                    Assert.Equal(2, coins.Length);
                    var newCoin = coins.First(c => (Money)c.Value == Money.Satoshis(500));
                    await payjoinRepository.TryLock(newCoin.OutPoint);
                });


                // Additional fee is 96 , minrelaytx is 294
                // We pay correctly, fees partially taken from what is overpaid
                // We paid 510, the receiver pay 10 sat
                // The send pay remaining 86 sat from his pocket
                // So total paid by sender should be 86 + 510 + 200 so we should get 1090 - (86 + 510 + 200) == 294 back)
                vector = (SpentCoin: Money.Satoshis(1090), InvoiceAmount: Money.Satoshis(500), Paid: Money.Satoshis(510), Fee: Money.Satoshis(200), ExpectLocked: true, ExpectedError: null as string);
                var proposedPSBT = await RunVector();
                Assert.Equal(2, proposedPSBT.Outputs.Count);
                Assert.Contains(proposedPSBT.Outputs, o => o.Value == Money.Satoshis(500) + receiverCoin.Amount);
                Assert.Contains(proposedPSBT.Outputs, o => o.Value == Money.Satoshis(294));
                proposedPSBT = await senderUser.Sign(proposedPSBT);
                proposedPSBT = proposedPSBT.Finalize();
                var explorerClient = tester.PayTester.GetService<ExplorerClientProvider>().GetExplorerClient(proposedPSBT.Network.NetworkSet.CryptoCode);
                var result = await explorerClient.BroadcastAsync(proposedPSBT.ExtractTransaction());
                Assert.True(result.Success);
                // Let's make sure that the coinjoin is not over paying, since the 10 overpaid sats have gone to fee
                await TestUtils.EventuallyAsync(async () =>
                {
                    var invoice = await tester.PayTester.GetService<InvoiceRepository>().GetInvoice(lastInvoiceId);
                    Assert.Equal(InvoiceStatus.Paid, invoice.Status);
                    Assert.Equal(InvoiceExceptionStatus.None, invoice.ExceptionStatus);
                    var coins = await btcPayWallet.GetUnspentCoins(receiverUser.DerivationScheme);
                    foreach (var coin in coins)
                        await payjoinRepository.TryLock(coin.OutPoint);
                });
                tester.ExplorerNode.Generate(1);
                receiverCoin = await receiverUser.ReceiveUTXO(Money.Satoshis(810), network);
                // Same as above. Except the sender send one satoshi less, so the change
                // output get below dust and should be removed completely.
                vector = (SpentCoin: Money.Satoshis(1089), InvoiceAmount: Money.Satoshis(500), Paid: Money.Satoshis(510), Fee: Money.Satoshis(200), ExpectLocked: true, ExpectedError: null as string);
                proposedPSBT = await RunVector();
                var output = Assert.Single(proposedPSBT.Outputs);
                // The 10 sats should still be paid, we removed one output but added one
                // sig, so we still need to pay some sats
                Assert.Equal(Money.Satoshis(500) + receiverCoin.Amount, output.Value);
                proposedPSBT = await senderUser.Sign(proposedPSBT);
                proposedPSBT = proposedPSBT.Finalize();
                explorerClient = tester.PayTester.GetService<ExplorerClientProvider>().GetExplorerClient(proposedPSBT.Network.NetworkSet.CryptoCode);
                result = await explorerClient.BroadcastAsync(proposedPSBT.ExtractTransaction(), true);
                Assert.True(result.Success);
            }
        }

        [Fact]
        // [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUseBIP79()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                
                ////var payJoinStateProvider = tester.PayTester.GetService<PayJoinStateProvider>();
                var btcPayNetwork = tester.NetworkProvider.GetNetwork<BTCPayNetwork>("BTC");
                var btcPayWallet = tester.PayTester.GetService<BTCPayWalletProvider>().GetWallet(btcPayNetwork);
                var cashCow = tester.ExplorerNode;
                cashCow.Generate(2); // get some money in case

                var senderUser = tester.NewAccount();
                senderUser.GrantAccess(true);
                senderUser.RegisterDerivationScheme("BTC", true, true);

                var invoice = senderUser.BitPay.CreateInvoice(
                    new Invoice() {Price = 100, Currency = "USD", FullNotifications = true});
                //payjoin is not enabled by default.
                Assert.DoesNotContain("bpu", invoice.CryptoInfo.First().PaymentUrls.BIP21);
                cashCow.SendToAddress(BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network),
                    Money.Coins(0.06m));

                var receiverUser = tester.NewAccount();
                receiverUser.GrantAccess(true);
                receiverUser.RegisterDerivationScheme("BTC", true, true);

                await receiverUser.EnablePayJoin();
                // payjoin is enabled, with a segwit wallet, and the keys are available in nbxplorer
                invoice = receiverUser.BitPay.CreateInvoice(
                    new Invoice() {Price = 0.02m, Currency = "BTC", FullNotifications = true});
                cashCow.SendToAddress(BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network),
                    Money.Coins(0.06m));
                var receiverWalletId = new WalletId(receiverUser.StoreId, "BTC");

                //give the cow some cash
                await cashCow.GenerateAsync(1);
                //let's get some more utxos first
                await receiverUser.ReceiveUTXO(Money.Coins(0.011m), btcPayNetwork);
                await receiverUser.ReceiveUTXO(Money.Coins(0.012m), btcPayNetwork);
                await receiverUser.ReceiveUTXO(Money.Coins(0.013m), btcPayNetwork);
                await receiverUser.ReceiveUTXO(Money.Coins(0.014m), btcPayNetwork);
                await senderUser.ReceiveUTXO(Money.Coins(0.021m), btcPayNetwork);
                await senderUser.ReceiveUTXO(Money.Coins(0.022m), btcPayNetwork);
                await senderUser.ReceiveUTXO(Money.Coins(0.023m), btcPayNetwork);
                await senderUser.ReceiveUTXO(Money.Coins(0.024m), btcPayNetwork);
                await senderUser.ReceiveUTXO(Money.Coins(0.025m), btcPayNetwork);
                await senderUser.ReceiveUTXO(Money.Coins(0.026m), btcPayNetwork);
                var senderChange = await senderUser.GetNewAddress(btcPayNetwork);

                //Let's start the harassment
                invoice = receiverUser.BitPay.CreateInvoice(
                    new Invoice() {Price = 0.02m, Currency = "BTC", FullNotifications = true});

                var parsedBip21 = new BitcoinUrlBuilder(invoice.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);

                var invoice2 = receiverUser.BitPay.CreateInvoice(
                    new Invoice() {Price = 0.02m, Currency = "BTC", FullNotifications = true});
                var secondInvoiceParsedBip21 = new BitcoinUrlBuilder(invoice2.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);

                var senderStore = await tester.PayTester.StoreRepository.FindStore(senderUser.StoreId);
                var paymentMethodId = new PaymentMethodId("BTC", PaymentTypes.BTCLike);
                var derivationSchemeSettings = senderStore.GetSupportedPaymentMethods(tester.NetworkProvider)
                    .OfType<DerivationSchemeSettings>().SingleOrDefault(settings =>
                        settings.PaymentId == paymentMethodId);

                ReceivedCoin[] senderCoins = null;
                await TestUtils.EventuallyAsync(async () =>
                {
                    senderCoins = await btcPayWallet.GetUnspentCoins(senderUser.DerivationScheme);
                    Assert.Contains(senderCoins, coin => coin.Value.GetValue(btcPayNetwork) == 0.026m);
                });
                var coin = senderCoins.Single(coin => coin.Value.GetValue(btcPayNetwork) == 0.021m);
                var coin2 = senderCoins.Single(coin => coin.Value.GetValue(btcPayNetwork) == 0.022m);
                var coin3 = senderCoins.Single(coin => coin.Value.GetValue(btcPayNetwork) == 0.023m);
                var coin4 = senderCoins.Single(coin => coin.Value.GetValue(btcPayNetwork) == 0.024m);
                var coin5 = senderCoins.Single(coin => coin.Value.GetValue(btcPayNetwork) == 0.025m);
                var coin6 = senderCoins.Single(coin => coin.Value.GetValue(btcPayNetwork) == 0.026m);

                var signingKeySettings = derivationSchemeSettings.GetSigningAccountKeySettings();
                signingKeySettings.RootFingerprint =
                    senderUser.GenerateWalletResponseV.MasterHDKey.GetPublicKey().GetHDFingerPrint();

                var extKey =
                    senderUser.GenerateWalletResponseV.MasterHDKey.Derive(signingKeySettings.GetRootedKeyPath()
                        .KeyPath);


                var n = tester.ExplorerClient.Network.NBitcoinNetwork;
                var Invoice1Coin1 = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
                    .SetChange(senderChange)
                    .Send(parsedBip21.Address, parsedBip21.Amount)
                    .AddCoins(coin.Coin)
                    .AddKeys(extKey.Derive(coin.KeyPath))
                    .SendEstimatedFees(new FeeRate(100m))
                    .BuildTransaction(true);

                var Invoice1Coin2 = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
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

                //Attempt 1: Send a signed tx to invoice 1 that does not pay the invoice at all 
                //Result: reject
                // Assert.False((await tester.PayTester.HttpClient.PostAsync(endpoint,
                //     new StringContent(Invoice2Coin1.ToHex(), Encoding.UTF8, "text/plain"))).IsSuccessStatusCode);

                //Attempt 2: Create two transactions using different inputs and send them to the same invoice. 
                //Result: Second Tx should be rejected. 
                var Invoice1Coin1ResponseTx = await senderUser.SubmitPayjoin(invoice, Invoice1Coin1, btcPayNetwork);
                await senderUser.SubmitPayjoin(invoice, Invoice1Coin1, btcPayNetwork, "already-paid");
                var contributedInputsInvoice1Coin1ResponseTx =
                    Invoice1Coin1ResponseTx.Inputs.Where(txin => coin.OutPoint != txin.PrevOut);
                Assert.Single(contributedInputsInvoice1Coin1ResponseTx);

                //Attempt 3: Send the same inputs from invoice 1 to invoice 2 while invoice 1 tx has not been broadcasted
                //Result: Reject Tx1 but accept tx 2 as its inputs were never accepted by invoice 1
                await senderUser.SubmitPayjoin(invoice2, Invoice2Coin1, btcPayNetwork, "inputs-already-used");
                var Invoice2Coin2ResponseTx = await senderUser.SubmitPayjoin(invoice2, Invoice2Coin2, btcPayNetwork);
                
                var contributedInputsInvoice2Coin2ResponseTx =
                    Invoice2Coin2ResponseTx.Inputs.Where(txin => coin2.OutPoint != txin.PrevOut);
                Assert.Single(contributedInputsInvoice2Coin2ResponseTx);

                //Attempt 4: Make tx that pays invoice 3 and 4 and submit to both
                //Result: reject on 4: the protocol should not worry about this complexity

                var invoice3 = receiverUser.BitPay.CreateInvoice(
                    new Invoice() {Price = 0.01m, Currency = "BTC", FullNotifications = true});
                var invoice3ParsedBip21 = new BitcoinUrlBuilder(invoice3.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);


                var invoice4 = receiverUser.BitPay.CreateInvoice(
                    new Invoice() {Price = 0.01m, Currency = "BTC", FullNotifications = true});
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
                    new Invoice() {Price = 0.01m, Currency = "BTC", FullNotifications = true});
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
                    new Invoice() {Price = 0.01m, Currency = "BTC", FullNotifications = true});
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

                var Invoice6Coin5Response1Tx =await senderUser.SubmitPayjoin(invoice6, invoice6Coin5, btcPayNetwork);
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
                    new Invoice() {Price = 0.01m, Currency = "BTC", FullNotifications = true});
                var invoice7ParsedBip21 = new BitcoinUrlBuilder(invoice7.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);

                var invoice7Coin6TxBuilder = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
                    .SetChange(senderChange)
                    .Send(invoice7ParsedBip21.Address, invoice7ParsedBip21.Amount)
                    .AddCoins(coin6.Coin)
                    .AddKeys(extKey.Derive(coin6.KeyPath))
                    .SendEstimatedFees(new FeeRate(100m))
                    .SetLockTime(0);

                var invoice7Coin6Tx = invoice7Coin6TxBuilder
                    .BuildTransaction(true);

                var invoice7Coin6Response1Tx = await senderUser.SubmitPayjoin(invoice7, invoice7Coin6Tx, btcPayNetwork);
                var Invoice7Coin6Response1TxSigned = invoice7Coin6TxBuilder.SignTransaction(invoice7Coin6Response1Tx);
                var contributedInputsInvoice7Coin6Response1TxSigned =
                    Invoice7Coin6Response1TxSigned.Inputs.Single(txin => coin6.OutPoint != txin.PrevOut);
                
                
                ////var receiverWalletPayJoinState = payJoinStateProvider.Get(receiverWalletId);
                ////Assert.Contains(receiverWalletPayJoinState.GetRecords(), item => item.InvoiceId == invoice7.Id);
                //broadcast the payjoin
                var res = (await tester.ExplorerClient.BroadcastAsync(Invoice7Coin6Response1TxSigned));
                Assert.True(res.Success);
                
                // Paid with coinjoin
                await TestUtils.EventuallyAsync(async () =>
                {
                    var invoiceEntity = await tester.PayTester.GetService<InvoiceRepository>().GetInvoice(invoice7.Id);
                    Assert.Equal(InvoiceStatus.Paid, invoiceEntity.Status);
                    Assert.Contains(invoiceEntity.GetPayments(), p => p.Accounted && ((BitcoinLikePaymentData)p.GetCryptoPaymentData()).PayjoinInformation.Type is PayjoinTransactionType.Coinjoin);
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
                    Assert.True(invoiceEntity.GetPayments().All(p => !p.Accounted));
                    ourOutpoint = invoiceEntity.GetAllBitcoinPaymentData().First().PayjoinInformation.OurOutpoints[0];
                });
                var payjoinRepository = tester.PayTester.GetService<PayJoinRepository>();
                // The outpoint should now be available for next pj selection
                Assert.False(await payjoinRepository.TryUnlock(ourOutpoint));
            }
        }
    }
}
