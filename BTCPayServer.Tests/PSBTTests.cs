using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class PSBTTests : UnitTestBase
    {
        public PSBTTests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        [Trait("Selenium", "Selenium")]
        public async Task CanPlayWithPSBT()
        {
            using var s = CreateSeleniumTester(newDb: true);
            await s.StartAsync();

            var u1 = s.RegisterNewUser(true);
            var hot = s.CreateNewStore();
            var seed = s.GenerateWallet(isHotWallet: true);
            var cold = s.CreateNewStore();
            s.GenerateWallet(isHotWallet: false, seed: seed.ToString());

            // Scenario 1: one user has two stores sharing same seed
            // one store is hot wallet, the other not.

            // Here, the cold wallet create a PSBT, then we switch to hot wallet to sign
            // the PSBT and broadcast
            s.GoToStore(cold.storeId);
            var address = await s.FundStoreWallet();
            Thread.Sleep(1000);
            s.GoToWallet(navPages: Views.Wallets.WalletsNavPages.Send);
            SendAllTo(s, address);
            s.Driver.FindElement(By.Id("SignWithPSBT")).Click();

            var psbt = ExtractPSBT(s);

            s.GoToStore(hot.storeId);
            s.GoToWallet(navPages: Views.Wallets.WalletsNavPages.PSBT);
            s.Driver.FindElement(By.Name("PSBT")).SendKeys(psbt);
            s.Driver.FindElement(By.Id("Decode")).Click();
            s.Driver.FindElement(By.Id("SignTransaction")).Click();
            s.Driver.FindElement(By.Id("BroadcastTransaction")).Click();
            s.FindAlertMessage();

            // Scenario 2: Same as scenario 1, except we create a PSBT from hot wallet, then sign by manually
            // entering the seed on the cold wallet.
            s.GoToWallet(navPages: Views.Wallets.WalletsNavPages.Send);
            SendAllTo(s, address);
            psbt = ExtractPSBT(s);

            // Let's check it has been signed, then remove the signature.
            // Also remove the hdkeys so we can test the update later
            var psbtParsed = PSBT.Parse(psbt, s.Server.NetworkProvider.BTC.NBitcoinNetwork);
            var signedPSBT = psbtParsed.Clone();
            Assert.True(psbtParsed.Clone().TryFinalize(out _));
            Assert.Single(psbtParsed.Inputs[0].PartialSigs);
            psbtParsed.Inputs[0].PartialSigs.Clear();
            Assert.Single(psbtParsed.Inputs[0].HDKeyPaths);
            psbtParsed.Inputs[0].HDKeyPaths.Clear();
            var skeletonPSBT = psbtParsed;

            s.GoToStore(cold.storeId);
            s.GoToWallet(navPages: Views.Wallets.WalletsNavPages.PSBT);
            s.Driver.FindElement(By.Name("PSBT")).SendKeys(skeletonPSBT.ToBase64());
            s.Driver.FindElement(By.Id("Decode")).Click();
            s.Driver.FindElement(By.Id("SignTransaction")).Click();
            s.Driver.FindElement(By.Id("SignWithSeed")).Click();
            s.Driver.FindElement(By.Name("SeedOrKey")).SendKeys(seed.ToString());
            s.Driver.FindElement(By.Id("Submit")).Click();
            s.Driver.FindElement(By.Id("BroadcastTransaction")).Click();
            s.FindAlertMessage();

            // Let's check if the update feature works
            s.GoToWallet(navPages: Views.Wallets.WalletsNavPages.PSBT);
            s.Driver.FindElement(By.Name("PSBT")).SendKeys(skeletonPSBT.ToBase64());
            s.Driver.FindElement(By.Id("Decode")).Click();
            s.Driver.FindElement(By.Id("PSBTOptionsAdvancedHeader")).Click();
            s.Driver.WaitForElement(By.Id("update-psbt")).Click();

            psbt = ExtractPSBT(s);
            psbtParsed = PSBT.Parse(psbt, s.Server.NetworkProvider.BTC.NBitcoinNetwork);
            Assert.Single(psbtParsed.Inputs[0].HDKeyPaths);
            Assert.Empty(psbtParsed.Inputs[0].PartialSigs);

            // Let's if we can combine the updated psbt (which has hdkeys, but no sig)
            // with the signed psbt (which has sig, but no hdkeys)
            s.GoToWallet(navPages: Views.Wallets.WalletsNavPages.PSBT);
            s.Driver.FindElement(By.Name("PSBT")).SendKeys(psbtParsed.ToBase64());
            s.Driver.FindElement(By.Id("Decode")).Click();
            s.Driver.FindElement(By.Id("PSBTOptionsAdvancedHeader")).Click();
            s.Driver.WaitForElement(By.Id("combine-psbt")).Click();
            signedPSBT.Inputs[0].HDKeyPaths.Clear();
            s.Driver.FindElement(By.Name("PSBT")).SendKeys(signedPSBT.ToBase64());
            s.Driver.WaitForElement(By.Id("Submit")).Click();

            psbt = ExtractPSBT(s);
            psbtParsed = PSBT.Parse(psbt, s.Server.NetworkProvider.BTC.NBitcoinNetwork);
            Assert.Single(psbtParsed.Inputs[0].HDKeyPaths);
            Assert.Single(psbtParsed.Inputs[0].PartialSigs);
        }

        private static void SendAllTo(SeleniumTester s, string address)
        {
            s.Driver.FindElement(By.Name("Outputs[0].DestinationAddress")).SendKeys(address);
            s.Driver.FindElement(By.ClassName("crypto-balance-link")).Click();
            s.Driver.FindElement(By.Id("SignTransaction")).Click();
        }

        private static string ExtractPSBT(SeleniumTester s)
        {
            var pageSource = s.Driver.PageSource;
            var start = pageSource.IndexOf("id=\"psbt-base64\">");
            start += "id=\"psbt-base64\">".Length;
            var end = pageSource.IndexOf("<", start);
            return pageSource[start..end];
        }
    }
}
