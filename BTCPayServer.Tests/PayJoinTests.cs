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
using BTCPayServer.Services.Wallets;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBitcoin.Payment;
using NBitpayClient;
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
        // [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUseBIP79()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
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

                //check that the BIP21 has an endpoint
                var bip21 = invoice.CryptoInfo.First().PaymentUrls.BIP21;
                Assert.Contains("bpu", bip21);
                var parsedBip21 = new BitcoinUrlBuilder(bip21, tester.ExplorerClient.Network.NBitcoinNetwork);
                var endpoint = parsedBip21.UnknowParameters["bpu"];


                //see if the btcpay send wallet supports BIP21 properly and also the payjoin endpoint
                var receiverWalletId = new WalletId(receiverUser.StoreId, "BTC");
                var senderWalletId = new WalletId(senderUser.StoreId, "BTC");
                var senderWallerController = senderUser.GetController<WalletsController>();
                var senderWalletSendVM = await senderWallerController.WalletSend(senderWalletId)
                    .AssertViewModelAsync<WalletSendModel>();
                senderWalletSendVM = await senderWallerController
                    .WalletSend(senderWalletId, senderWalletSendVM, "", CancellationToken.None, bip21)
                    .AssertViewModelAsync<WalletSendModel>();

                Assert.Single(senderWalletSendVM.Outputs);
                Assert.Equal(endpoint, senderWalletSendVM.PayJoinEndpointUrl);
                Assert.Equal(parsedBip21.Address.ToString(), senderWalletSendVM.Outputs.First().DestinationAddress);
                Assert.Equal(parsedBip21.Amount.ToDecimal(MoneyUnit.BTC), senderWalletSendVM.Outputs.First().Amount);

                //the nbx wallet option should also be available
                Assert.True(senderWalletSendVM.NBXSeedAvailable);

                //pay the invoice with the nbx seed wallet option + also the invoice 
                var postRedirectViewModel = await senderWallerController.WalletSend(senderWalletId,
                        senderWalletSendVM, "nbx-seed", CancellationToken.None)
                    .AssertViewModelAsync<PostRedirectViewModel>();
                var redirectedPSBT = postRedirectViewModel.Parameters.Single(p => p.Key == "psbt").Value;
                var psbt = PSBT.Parse(redirectedPSBT, tester.ExplorerClient.Network.NBitcoinNetwork);
                var senderWalletSendPSBTResult = new WalletPSBTReadyViewModel()
                {
                    PSBT = redirectedPSBT,
                    SigningKeyPath = postRedirectViewModel.Parameters.Single(p => p.Key == "SigningKeyPath").Value,
                    SigningKey = postRedirectViewModel.Parameters.Single(p => p.Key == "SigningKey").Value
                };
                //While the endpoint was set, the receiver had no utxos. The payment should fall back to original payment terms instead
                Assert.Equal(parsedBip21.Amount.ToDecimal(MoneyUnit.BTC).ToString(),
                    psbt.Outputs.Single(model => model.ScriptPubKey == parsedBip21.Address.ScriptPubKey).Value);

                Assert.Equal("WalletTransactions",
                    Assert.IsType<RedirectToActionResult>(
                            await senderWallerController.WalletPSBTReady(senderWalletId, senderWalletSendPSBTResult,
                                "broadcast"))
                        .ActionName);

                //we used the bip21 link straight away to pay the invoice so it should be paid straight away. 
                TestUtils.Eventually(() =>
                {
                    invoice = receiverUser.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(Invoice.STATUS_PAID, invoice.Status);
                });
                //verify that there is nothing in the payment state
                
                var payJoinStateProvider = tester.PayTester.GetService<PayJoinStateProvider>();
                var receiverWalletPayJoinState = payJoinStateProvider.Get(receiverWalletId);
                Assert.NotNull(receiverWalletPayJoinState);
                Assert.Empty(receiverWalletPayJoinState.GetRecords());

                //now that there is a utxo, let's do it again

                invoice = receiverUser.BitPay.CreateInvoice(
                    new Invoice() {Price = 0.02m, Currency = "BTC", FullNotifications = true});
                bip21 = invoice.CryptoInfo.First().PaymentUrls.BIP21;
                parsedBip21 = new BitcoinUrlBuilder(bip21, tester.ExplorerClient.Network.NBitcoinNetwork);
                senderWalletSendVM = await senderWallerController.WalletSend(senderWalletId)
                    .AssertViewModelAsync<WalletSendModel>();
                senderWalletSendVM = await senderWallerController
                    .WalletSend(senderWalletId, senderWalletSendVM, "", CancellationToken.None, bip21)
                    .AssertViewModelAsync<WalletSendModel>();
                postRedirectViewModel = await senderWallerController.WalletSend(senderWalletId,
                        senderWalletSendVM, "nbx-seed", CancellationToken.None)
                    .AssertViewModelAsync<PostRedirectViewModel>();
                redirectedPSBT = postRedirectViewModel.Parameters.Single(p => p.Key == "psbt").Value;
                psbt = PSBT.Parse(redirectedPSBT, tester.ExplorerClient.Network.NBitcoinNetwork);
                senderWalletSendPSBTResult = new WalletPSBTReadyViewModel()
                {
                    PSBT = redirectedPSBT,
                    SigningKeyPath = postRedirectViewModel.Parameters.Single(p => p.Key == "SigningKeyPath").Value,
                    SigningKey = postRedirectViewModel.Parameters.Single(p => p.Key == "SigningKey").Value
                };
                //the payjoin should make the amount being paid to the address higher
                Assert.True(parsedBip21.Amount.ToDecimal(MoneyUnit.BTC) < psbt.Outputs
                    .Single(model => model.ScriptPubKey == parsedBip21.Address.ScriptPubKey).Value
                    .ToDecimal(MoneyUnit.BTC));

                //the state should now hold that there is an ongoing utxo 
                Assert.Single(receiverWalletPayJoinState.GetRecords());
                Assert.Equal(0.02m, receiverWalletPayJoinState.GetRecords().First().ContributedAmount);
                Assert.Single(receiverWalletPayJoinState.GetRecords().First().CoinsExposed);
                Assert.False(receiverWalletPayJoinState.GetRecords().First().TxSeen);
                Assert.Equal(psbt.Finalize().ExtractTransaction().GetHash(),
                    receiverWalletPayJoinState.GetRecords().First().ProposedTransactionHash);

                Assert.Equal("WalletTransactions",
                    Assert.IsType<RedirectToActionResult>(
                            await senderWallerController.WalletPSBTReady(senderWalletId, senderWalletSendPSBTResult,
                                "broadcast"))
                        .ActionName);

                TestUtils.Eventually(() =>
                {
                    invoice = receiverUser.BitPay.GetInvoice(invoice.Id);

                    Assert.Equal(Invoice.STATUS_PAID, invoice.Status);
                    Assert.Equal(Invoice.EXSTATUS_FALSE, invoice.ExceptionStatus.ToString().ToLowerInvariant());
                });

                //verify that we have a record that it was a payjoin
                var receiverController = receiverUser.GetController<InvoiceController>();
                var invoiceVM =
                    await receiverController.Invoice(invoice.Id).AssertViewModelAsync<InvoiceDetailsModel>();
                Assert.Single(invoiceVM.Payments);
                Assert.True(Assert.IsType<BitcoinLikePaymentData>(invoiceVM.Payments.First().GetCryptoPaymentData())
                    .PayJoinSelfContributedAmount > 0);

                
                //we dont remove the payjoin tx state even if we detect it, for cases of RBF
                Assert.NotEmpty(receiverWalletPayJoinState.GetRecords());
                Assert.Single(receiverWalletPayJoinState.GetRecords());
                Assert.True(receiverWalletPayJoinState.GetRecords().First().TxSeen);
                
                var debugData = new
                {
                    StoreId = receiverWalletId.StoreId,
                    InvoiceId = receiverWalletPayJoinState.GetRecords().First().InvoiceId,
                    PayJoinTx = receiverWalletPayJoinState.GetRecords().First().ProposedTransactionHash
                };
                for (int i = 0; i < 6; i++)
                {
                    await tester.WaitForEvent<NewBlockEvent>(async () =>
                    {
                        await cashCow.GenerateAsync(1);
                    });
                }
                
                //check that the state has cleared that ongoing tx
                receiverWalletPayJoinState = payJoinStateProvider.Get(receiverWalletId);
                Assert.NotNull(receiverWalletPayJoinState);
                Assert.Empty(receiverWalletPayJoinState.GetRecords());
                Assert.Empty(receiverWalletPayJoinState.GetExposedCoins());

                //Cool, so the payjoin works!
                //The cool thing with payjoin is that your utxos don't grow
                Assert.Single(await btcPayWallet.GetUnspentCoins(receiverUser.DerivationScheme));

                //Let's be as malicious as CSW

                //give the cow some cash
                await cashCow.GenerateAsync(1);
                //let's get some more utxos first
                Assert.NotNull(await cashCow.SendToAddressAsync(
                    (await btcPayWallet.ReserveAddressAsync(receiverUser.DerivationScheme)).Address,
                    new Money(0.011m, MoneyUnit.BTC)));
                Assert.NotNull(await cashCow.SendToAddressAsync(
                    (await btcPayWallet.ReserveAddressAsync(receiverUser.DerivationScheme)).Address,
                    new Money(0.012m, MoneyUnit.BTC)));
                Assert.NotNull(await cashCow.SendToAddressAsync(
                    (await btcPayWallet.ReserveAddressAsync(receiverUser.DerivationScheme)).Address,
                    new Money(0.013m, MoneyUnit.BTC)));
                Assert.NotNull(await cashCow.SendToAddressAsync(
                    (await btcPayWallet.ReserveAddressAsync(senderUser.DerivationScheme)).Address,
                    new Money(0.021m, MoneyUnit.BTC)));
                Assert.NotNull(await cashCow.SendToAddressAsync(
                    (await btcPayWallet.ReserveAddressAsync(senderUser.DerivationScheme)).Address,
                    new Money(0.022m, MoneyUnit.BTC)));
                Assert.NotNull(await cashCow.SendToAddressAsync(
                    (await btcPayWallet.ReserveAddressAsync(senderUser.DerivationScheme)).Address,
                    new Money(0.023m, MoneyUnit.BTC)));
                Assert.NotNull(await cashCow.SendToAddressAsync(
                    (await btcPayWallet.ReserveAddressAsync(senderUser.DerivationScheme)).Address,
                    new Money(0.024m, MoneyUnit.BTC)));
                Assert.NotNull(await cashCow.SendToAddressAsync(
                    (await btcPayWallet.ReserveAddressAsync(senderUser.DerivationScheme)).Address,
                    new Money(0.025m, MoneyUnit.BTC)));
                Assert.NotNull(await cashCow.SendToAddressAsync(
                    (await btcPayWallet.ReserveAddressAsync(senderUser.DerivationScheme)).Address,
                    new Money(0.026m, MoneyUnit.BTC)));

                await cashCow.SendToAddressAsync(
                    (await btcPayWallet.ReserveAddressAsync(senderUser.DerivationScheme)).Address,
                    new Money(0.014m, MoneyUnit.BTC));
                var senderCoins = await btcPayWallet.GetUnspentCoins(senderUser.DerivationScheme);

                var senderChange = (await btcPayWallet.GetChangeAddressAsync(senderUser.DerivationScheme)).Item1;

                //Let's start the harassment
                invoice = receiverUser.BitPay.CreateInvoice(
                    new Invoice() {Price = 0.02m, Currency = "BTC", FullNotifications = true});

                parsedBip21 = new BitcoinUrlBuilder(invoice.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);
                endpoint = parsedBip21.UnknowParameters["bpu"];

                var invoice2 = receiverUser.BitPay.CreateInvoice(
                    new Invoice() {Price = 0.02m, Currency = "BTC", FullNotifications = true});
                var secondInvoiceParsedBip21 = new BitcoinUrlBuilder(invoice2.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);
                var endpoint2 = secondInvoiceParsedBip21.UnknowParameters["bpu"];

                var senderStore = await tester.PayTester.StoreRepository.FindStore(senderUser.StoreId);
                var paymentMethodId = new PaymentMethodId("BTC", PaymentTypes.BTCLike);
                var derivationSchemeSettings = senderStore.GetSupportedPaymentMethods(tester.NetworkProvider)
                    .OfType<DerivationSchemeSettings>().SingleOrDefault(settings =>
                        settings.PaymentId == paymentMethodId);
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
                Assert.False((await tester.PayTester.HttpClient.PostAsync(endpoint,
                    new StringContent(Invoice2Coin1.ToHex(), Encoding.UTF8, "text/plain"))).IsSuccessStatusCode);

                //Attempt 2: Create two transactions using different inputs and send them to the same invoice. 
                //Result: Second Tx should be rejected. 
                var Invoice1Coin1Response = await tester.PayTester.HttpClient.PostAsync(endpoint,
                    new StringContent(Invoice1Coin1.ToHex(), Encoding.UTF8, "text/plain"));

                var Invoice1Coin2Response = await tester.PayTester.HttpClient.PostAsync(endpoint,
                    new StringContent(Invoice1Coin2.ToHex(), Encoding.UTF8, "text/plain"));

                Assert.True(Invoice1Coin1Response.IsSuccessStatusCode);
                Assert.False(Invoice1Coin2Response.IsSuccessStatusCode);
                var Invoice1Coin1ResponseTx =
                    Transaction.Parse(await Invoice1Coin1Response.Content.ReadAsStringAsync(), n);
                var contributedInputsInvoice1Coin1ResponseTx =
                    Invoice1Coin1ResponseTx.Inputs.Where(txin => coin.OutPoint != txin.PrevOut);
                Assert.Single(contributedInputsInvoice1Coin1ResponseTx);

                //Attempt 3: Send the same inputs from invoice 1 to invoice 2 while invoice 1 tx has not been broadcasted
                //Result: Reject Tx1 but accept tx 2 as its inputs were never accepted by invoice 1

                var Invoice2Coin1Response = await tester.PayTester.HttpClient.PostAsync(endpoint2,
                    new StringContent(Invoice2Coin1.ToHex(), Encoding.UTF8, "text/plain"));

                var Invoice2Coin2Response = await tester.PayTester.HttpClient.PostAsync(endpoint2,
                    new StringContent(Invoice2Coin2.ToHex(), Encoding.UTF8, "text/plain"));

                Assert.False(Invoice2Coin1Response.IsSuccessStatusCode);
                Assert.True(Invoice2Coin2Response.IsSuccessStatusCode);

                var Invoice2Coin2ResponseTx =
                    Transaction.Parse(await Invoice2Coin2Response.Content.ReadAsStringAsync(), n);
                var contributedInputsInvoice2Coin2ResponseTx =
                    Invoice2Coin2ResponseTx.Inputs.Where(txin => coin2.OutPoint != txin.PrevOut);
                Assert.Single(contributedInputsInvoice2Coin2ResponseTx);

                //Attempt 4: Make tx that pays invoice 3 and 4 and submit to both
                //Result: reject on 4: the protocol should not worry about this complexity

                var invoice3 = receiverUser.BitPay.CreateInvoice(
                    new Invoice() {Price = 0.01m, Currency = "BTC", FullNotifications = true});
                var invoice3ParsedBip21 = new BitcoinUrlBuilder(invoice3.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);
                var invoice3Endpoint = invoice3ParsedBip21.UnknowParameters["bpu"];


                var invoice4 = receiverUser.BitPay.CreateInvoice(
                    new Invoice() {Price = 0.01m, Currency = "BTC", FullNotifications = true});
                var invoice4ParsedBip21 = new BitcoinUrlBuilder(invoice4.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);
                var invoice4Endpoint = invoice4ParsedBip21.UnknowParameters["bpu"];


                var Invoice3AndInvoice4Coin3 = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
                    .SetChange(senderChange)
                    .Send(invoice3ParsedBip21.Address, invoice3ParsedBip21.Amount)
                    .Send(invoice4ParsedBip21.Address, invoice4ParsedBip21.Amount)
                    .AddCoins(coin3.Coin)
                    .AddKeys(extKey.Derive(coin3.KeyPath))
                    .SendEstimatedFees(new FeeRate(100m))
                    .BuildTransaction(true);

                var Invoice3Coin3Response = await tester.PayTester.HttpClient.PostAsync(invoice3Endpoint,
                    new StringContent(Invoice3AndInvoice4Coin3.ToHex(), Encoding.UTF8, "text/plain"));

                var Invoice4Coin3Response = await tester.PayTester.HttpClient.PostAsync(invoice4Endpoint,
                    new StringContent(Invoice3AndInvoice4Coin3.ToHex(), Encoding.UTF8, "text/plain"));

                Assert.True(Invoice3Coin3Response.IsSuccessStatusCode);
                Assert.False(Invoice4Coin3Response.IsSuccessStatusCode);

                //Attempt 5: Make tx that pays invoice 5 with 2 outputs
                //Result: proposed tx consolidates the outputs

                var invoice5 = receiverUser.BitPay.CreateInvoice(
                    new Invoice() {Price = 0.01m, Currency = "BTC", FullNotifications = true});
                var invoice5ParsedBip21 = new BitcoinUrlBuilder(invoice5.CryptoInfo.First().PaymentUrls.BIP21,
                    tester.ExplorerClient.Network.NBitcoinNetwork);
                var invoice5Endpoint = invoice5ParsedBip21.UnknowParameters["bpu"];

                var Invoice5Coin4TxBuilder = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
                    .SetChange(senderChange)
                    .Send(invoice5ParsedBip21.Address, invoice5ParsedBip21.Amount / 2)
                    .Send(invoice5ParsedBip21.Address, invoice5ParsedBip21.Amount / 2)
                    .AddCoins(coin4.Coin)
                    .AddKeys(extKey.Derive(coin4.KeyPath))
                    .SendEstimatedFees(new FeeRate(100m));

                var Invoice5Coin4 = Invoice5Coin4TxBuilder.BuildTransaction(true);

                var Invoice5Coin4Response = await tester.PayTester.HttpClient.PostAsync(invoice5Endpoint,
                    new StringContent(Invoice5Coin4.ToHex(), Encoding.UTF8, "text/plain"));

                Assert.True(Invoice5Coin4Response.IsSuccessStatusCode);
                var Invoice5Coin4ResponseTx =
                    Transaction.Parse(await Invoice5Coin4Response.Content.ReadAsStringAsync(), n);
                Assert.Single(Invoice5Coin4ResponseTx.Outputs.To(invoice5ParsedBip21.Address));

                //Attempt 6: submit the same tx over and over in the hopes of getting new utxos
                //Result: same tx gets sent back 
                for (int i = 0; i < 5; i++)
                {
                    var Invoice5Coin4Response2 = await tester.PayTester.HttpClient.PostAsync(invoice5Endpoint,
                        new StringContent(Invoice5Coin4.ToHex(), Encoding.UTF8, "text/plain"));
                    if (!Invoice5Coin4Response2.IsSuccessStatusCode)
                    {
                        Logs.Tester.LogInformation(
                            $"Failed on try {i + 1} with {await Invoice5Coin4Response2.Content.ReadAsStringAsync()}");
                    }

                    Assert.True(Invoice5Coin4Response2.IsSuccessStatusCode);
                    var Invoice5Coin4Response2Tx =
                        Transaction.Parse(await Invoice5Coin4Response2.Content.ReadAsStringAsync(), n);
                    Assert.Equal(Invoice5Coin4ResponseTx.GetHash(), Invoice5Coin4Response2Tx.GetHash());
                }

                //Attempt 7: send the payjoin porposed tx to the endpoint 
                //Result: get same tx sent back as is
                Invoice5Coin4Response = await tester.PayTester.HttpClient.PostAsync(invoice5Endpoint,
                    new StringContent(Invoice5Coin4.ToHex(), Encoding.UTF8, "text/plain"));
                Assert.True(Invoice5Coin4Response.IsSuccessStatusCode);
                Assert.Equal(Invoice5Coin4ResponseTx.GetHash(),
                    Transaction.Parse(await Invoice5Coin4Response.Content.ReadAsStringAsync(), n).GetHash());

                //Attempt 8: sign the payjoin and send it back to the endpoint
                //Result: get same tx sent back as is
                var Invoice5Coin4ResponseTxSigned = Invoice5Coin4TxBuilder.SignTransaction(Invoice5Coin4ResponseTx);
                Invoice5Coin4Response = await tester.PayTester.HttpClient.PostAsync(invoice5Endpoint,
                    new StringContent(Invoice5Coin4.ToHex(), Encoding.UTF8, "text/plain"));
                Assert.True(Invoice5Coin4Response.IsSuccessStatusCode);
                Assert.Equal(Invoice5Coin4ResponseTxSigned.GetHash(),
                    Transaction.Parse(await Invoice5Coin4Response.Content.ReadAsStringAsync(), n).GetHash());

                //Attempt 9: broadcast a payjoin tx, then try to submit both original tx and the payjoin itself again
                //Result: fails
                await tester.ExplorerClient.BroadcastAsync(Invoice5Coin4ResponseTxSigned);

                Assert.False((await tester.PayTester.HttpClient.PostAsync(invoice5Endpoint,
                    new StringContent(Invoice5Coin4.ToHex(), Encoding.UTF8, "text/plain"))).IsSuccessStatusCode);

                Assert.False((await tester.PayTester.HttpClient.PostAsync(invoice5Endpoint,
                    new StringContent(Invoice5Coin4.ToHex(), Encoding.UTF8, "text/plain"))).IsSuccessStatusCode);

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
                var invoice6Endpoint = invoice6ParsedBip21.UnknowParameters["bpu"];

                var invoice6Coin5TxBuilder = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
                    .SetChange(senderChange)
                    .Send(invoice6ParsedBip21.Address, invoice6ParsedBip21.Amount)
                    .AddCoins(coin5.Coin)
                    .AddKeys(extKey.Derive(coin5.KeyPath))
                    .SendEstimatedFees(new FeeRate(100m))
                    .SetLockTime(0);

                var invoice6Coin5 = invoice6Coin5TxBuilder
                    .BuildTransaction(true);

                var Invoice6Coin5Response1 = await tester.PayTester.HttpClient.PostAsync(invoice6Endpoint,
                    new StringContent(invoice6Coin5.ToHex(), Encoding.UTF8, "text/plain"));
                Assert.True(Invoice6Coin5Response1.IsSuccessStatusCode);
                var Invoice6Coin5Response1Tx =
                    Transaction.Parse(await Invoice6Coin5Response1.Content.ReadAsStringAsync(), n);
                var Invoice6Coin5Response1TxSigned = invoice6Coin5TxBuilder.SignTransaction(Invoice6Coin5Response1Tx);
                //broadcast the first payjoin
                await tester.ExplorerClient.BroadcastAsync(Invoice6Coin5Response1TxSigned);

                invoice6Coin5TxBuilder = invoice6Coin5TxBuilder.SendEstimatedFees(new FeeRate(100m));
                var invoice6Coin5Bumpedfee = invoice6Coin5TxBuilder
                    .BuildTransaction(true);

                var Invoice6Coin5Response3 = await tester.PayTester.HttpClient.PostAsync(invoice6Endpoint,
                    new StringContent(invoice6Coin5Bumpedfee.ToHex(), Encoding.UTF8, "text/plain"));
                Assert.True(Invoice6Coin5Response3.IsSuccessStatusCode);
                var Invoice6Coin5Response3Tx =
                    Transaction.Parse(await Invoice6Coin5Response3.Content.ReadAsStringAsync(), n);
                Assert.True(invoice6Coin5Bumpedfee.Inputs.All(txin =>
                    Invoice6Coin5Response3Tx.Inputs.Any(txin2 => txin2.PrevOut == txin.PrevOut)));

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
                var invoice7Endpoint = invoice7ParsedBip21.UnknowParameters["bpu"];

                var invoice7Coin6TxBuilder = tester.ExplorerClient.Network.NBitcoinNetwork.CreateTransactionBuilder()
                    .SetChange(senderChange)
                    .Send(invoice7ParsedBip21.Address, invoice7ParsedBip21.Amount)
                    .AddCoins(coin6.Coin)
                    .AddKeys(extKey.Derive(coin6.KeyPath))
                    .SendEstimatedFees(new FeeRate(100m))
                    .SetLockTime(0);

                var invoice7Coin6Tx = invoice7Coin6TxBuilder
                    .BuildTransaction(true);

                var invoice7Coin6Response1 = await tester.PayTester.HttpClient.PostAsync(invoice7Endpoint,
                    new StringContent(invoice7Coin6Tx.ToHex(), Encoding.UTF8, "text/plain"));
                Assert.True(invoice7Coin6Response1.IsSuccessStatusCode);
                var invoice7Coin6Response1Tx =
                    Transaction.Parse(await invoice7Coin6Response1.Content.ReadAsStringAsync(), n);
                var Invoice7Coin6Response1TxSigned = invoice7Coin6TxBuilder.SignTransaction(invoice7Coin6Response1Tx);
                var contributedInputsInvoice7Coin6Response1TxSigned =
                    Invoice7Coin6Response1TxSigned.Inputs.Single(txin => coin6.OutPoint != txin.PrevOut);
                Assert.Contains(receiverWalletPayJoinState.GetRecords(), item => item.InvoiceId == invoice7.Id);
                //broadcast the payjoin
                await tester.WaitForEvent<InvoiceEvent>(async () =>
                {
                    var res = (await tester.ExplorerClient.BroadcastAsync(Invoice7Coin6Response1TxSigned));
                    Assert.True(res.Success);
                });

                Assert.Contains(receiverWalletPayJoinState.GetRecords(), item => item.InvoiceId == invoice7.Id && item.TxSeen);

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
                await tester.WaitForEvent<InvoiceEvent>(async () =>
                {
                    var res = (await tester.ExplorerClient.BroadcastAsync(invoice7Coin6Tx2));
                    Assert.True(res.Success);
                });
                //btcpay does not know of replaced txs where the outputs do not pay it(double spends using RBF to "cancel" a payment)
                Assert.Contains(receiverWalletPayJoinState.GetRecords(), item => item.InvoiceId == invoice7.Id && item.TxSeen);

                //hijack our automated payjoin original broadcaster and force it to broadcast all, now
                var payJoinTransactionBroadcaster = tester.PayTester.ServiceProvider.GetServices<IHostedService>()
                    .OfType<PayJoinTransactionBroadcaster>().First();
                await payJoinTransactionBroadcaster.BroadcastStaleTransactions(TimeSpan.Zero, CancellationToken.None);

                Assert.DoesNotContain(receiverWalletPayJoinState.GetRecords(), item => item.InvoiceId == invoice7.Id);
                //all our failed payjoins are clear and any exposed utxo has been moved to the prioritized list
                Assert.Contains(receiverWalletPayJoinState.GetExposedCoins(), receivedCoin =>
                    receivedCoin.OutPoint == contributedInputsInvoice7Coin6Response1TxSigned.PrevOut);
            }
        }
    }
}
