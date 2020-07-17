using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class PSBTTests
    {
        public PSBTTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
        }
        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanPlayWithPSBT()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                var invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 10,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some \", description",
                    FullNotifications = true
                }, Facade.Merchant);
                var cashCow = tester.ExplorerNode;
                var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, cashCow.Network);
                cashCow.SendToAddress(invoiceAddress, Money.Coins(1.5m));
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal("paid", invoice.Status);
                });

                var walletController = user.GetController<WalletsController>();
                var walletId = new WalletId(user.StoreId, "BTC");
                var sendDestination = new Key().PubKey.Hash.GetAddress(user.SupportedNetwork.NBitcoinNetwork).ToString();
                var sendModel = new WalletSendModel()
                {
                    Outputs = new List<WalletSendModel.TransactionOutput>()
                    {
                        new WalletSendModel.TransactionOutput()
                        {
                            DestinationAddress = sendDestination,
                            Amount = 0.1m,
                        }
                    },
                    FeeSatoshiPerByte = 1,
                    CurrentBalance = 1.5m
                };

                string redirectedPSBT = AssertRedirectedPSBT(await walletController.WalletSend(walletId, sendModel, command: "analyze-psbt"), nameof(walletController.WalletPSBT));
                var vmPSBT = await walletController.WalletPSBT(walletId, new WalletPSBTViewModel() { PSBT = redirectedPSBT }).AssertViewModelAsync<WalletPSBTViewModel>();
                var unsignedPSBT = PSBT.Parse(vmPSBT.PSBT, user.SupportedNetwork.NBitcoinNetwork);
                Assert.NotNull(vmPSBT.Decoded);

                var filePSBT = (FileContentResult)(await walletController.WalletPSBT(walletId, vmPSBT, "save-psbt"));
                PSBT.Load(filePSBT.FileContents, user.SupportedNetwork.NBitcoinNetwork);

                var vmPSBT2 = await walletController.WalletPSBTReady(walletId, new WalletPSBTReadyViewModel()
                {
                    SigningContext = new SigningContextModel()
                    {
                        PSBT = AssertRedirectedPSBT(await walletController.WalletPSBT(walletId, vmPSBT, "broadcast"), nameof(walletController.WalletPSBTReady))
                    }
                }).AssertViewModelAsync<WalletPSBTReadyViewModel>();
                Assert.NotEmpty(vmPSBT2.Inputs.Where(i => i.Error != null));
                Assert.Equal(vmPSBT.PSBT, vmPSBT2.SigningContext.PSBT);

                var signedPSBT = unsignedPSBT.Clone();
                signedPSBT.SignAll(user.DerivationScheme, user.GenerateWalletResponseV.AccountHDKey, user.GenerateWalletResponseV.AccountKeyPath);
                vmPSBT.PSBT = signedPSBT.ToBase64();
                var psbtReady = await walletController.WalletPSBTReady(walletId, new WalletPSBTReadyViewModel()
                {
                    SigningContext = new SigningContextModel()
                    {
                        PSBT = AssertRedirectedPSBT(await walletController.WalletPSBT(walletId, vmPSBT, "broadcast"), nameof(walletController.WalletPSBTReady))
                    }
                }).AssertViewModelAsync<WalletPSBTReadyViewModel>();
                Assert.Equal(2 + 1, psbtReady.Destinations.Count); // The fee is a destination
                Assert.Contains(psbtReady.Destinations, d => d.Destination == sendDestination && !d.Positive);
                Assert.Contains(psbtReady.Destinations, d => d.Positive);
                var redirect = Assert.IsType<RedirectToActionResult>(await walletController.WalletPSBTReady(walletId, psbtReady, command: "broadcast"));
                Assert.Equal(nameof(walletController.WalletTransactions), redirect.ActionName);

                vmPSBT.PSBT = unsignedPSBT.ToBase64();
                var combineVM = await walletController.WalletPSBT(walletId, vmPSBT, "combine").AssertViewModelAsync<WalletPSBTCombineViewModel>();
                Assert.Equal(vmPSBT.PSBT, combineVM.OtherPSBT);
                combineVM.PSBT = signedPSBT.ToBase64();
                var psbt = AssertRedirectedPSBT(await walletController.WalletPSBTCombine(walletId, combineVM), nameof(walletController.WalletPSBT));

                var signedPSBT2 = PSBT.Parse(psbt, user.SupportedNetwork.NBitcoinNetwork);
                Assert.True(signedPSBT.TryFinalize(out _));
                Assert.True(signedPSBT2.TryFinalize(out _));
                Assert.Equal(signedPSBT, signedPSBT2);

                // Can use uploaded file?
                combineVM.PSBT = null;
                combineVM.UploadedPSBTFile = TestUtils.GetFormFile("signedPSBT", signedPSBT.ToBytes());
                psbt = AssertRedirectedPSBT(await walletController.WalletPSBTCombine(walletId, combineVM), nameof(walletController.WalletPSBT));
                signedPSBT2 = PSBT.Parse(psbt, user.SupportedNetwork.NBitcoinNetwork);
                Assert.True(signedPSBT.TryFinalize(out _));
                Assert.True(signedPSBT2.TryFinalize(out _));
                Assert.Equal(signedPSBT, signedPSBT2);

                var ready = (await walletController.WalletPSBTReady(walletId, new WalletPSBTReadyViewModel()
                {
                    SigningContext = new SigningContextModel(signedPSBT)
                })).AssertViewModel<WalletPSBTReadyViewModel>();
                Assert.Equal(signedPSBT.ToBase64(), ready.SigningContext.PSBT);
                psbt = AssertRedirectedPSBT(await walletController.WalletPSBTReady(walletId, ready, command: "analyze-psbt"), nameof(walletController.WalletPSBT));
                Assert.Equal(signedPSBT.ToBase64(), psbt);
                redirect = Assert.IsType<RedirectToActionResult>(await walletController.WalletPSBTReady(walletId, ready, command: "broadcast"));
                Assert.Equal(nameof(walletController.WalletTransactions), redirect.ActionName);

                //test base64 psbt file
                Assert.False(string.IsNullOrEmpty(Assert.IsType<WalletPSBTViewModel>(
                    Assert.IsType<ViewResult>(
                        await walletController.WalletPSBT(walletId,
                            new WalletPSBTViewModel()
                            {
                                UploadedPSBTFile = TestUtils.GetFormFile("base64", signedPSBT.ToBase64())
                            })).Model).PSBT));
            }
        }

        private static string AssertRedirectedPSBT(IActionResult view, string actionName)
        {
            var postRedirectView = Assert.IsType<ViewResult>(view);
            var postRedirectViewModel = Assert.IsType<PostRedirectViewModel>(postRedirectView.Model);
            Assert.Equal(actionName, postRedirectViewModel.AspAction);
            var redirectedPSBT = postRedirectViewModel.Parameters.Single(p => p.Key == "psbt" || p.Key == "SigningContext.PSBT").Value;
            return redirectedPSBT;
        }
    }
}
