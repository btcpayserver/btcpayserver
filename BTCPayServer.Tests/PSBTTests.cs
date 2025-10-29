using System;
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
    public class PSBTTests(ITestOutputHelper helper) : UnitTestBase(helper)
    {
        [Fact]
        [Trait("Playwright", "Playwright")]
        public async Task CanPlayWithPSBT()
        {
            await using var s = CreatePlaywrightTester(newDb: true);
            await s.StartAsync();

            await s.RegisterNewUser(true);
            var hot = await s.CreateNewStore();
            var seed = await s.GenerateWallet(isHotWallet: true);
            var cold = await s.CreateNewStore();
            await s.GenerateWallet(isHotWallet: false, seed: seed.ToString());

            // Scenario 1: one user has two stores sharing same seed
            // one store is hot wallet, the other not.

            // Here, the cold wallet create a PSBT, then we switch to hot wallet to sign
            // the PSBT and broadcast
            await s.GoToStore(cold.storeId);
            var address = await s.FundStoreWallet();
            await Task.Delay(1000);
            await s.GoToWallet(navPages: Views.Wallets.WalletsNavPages.Send);
            await SendAllTo(s, address);
            await s.Page.ClickAsync("#SignWithPSBT");

            var psbt = await ExtractPSBT(s);

            await s.GoToStore(hot.storeId);
            await s.GoToWallet(s.WalletId, navPages: Views.Wallets.WalletsNavPages.PSBT);
            await s.Page.Locator("[name='PSBT']").FillAsync(psbt);
            await s.Page.ClickAsync("#Decode");
            await s.Page.ClickAsync("#SignTransaction");
            await s.Page.ClickAsync("#BroadcastTransaction");
            await s.FindAlertMessage();

            // Scenario 2: Same as scenario 1, except we create a PSBT from hot wallet, then sign by manually
            // entering the seed on the cold wallet.
            await s.GoToWallet(navPages: Views.Wallets.WalletsNavPages.Send);
            await SendAllTo(s, address);
            psbt = await ExtractPSBT(s);

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

            await s.GoToStore(cold.storeId);
            await s.GoToWallet(s.WalletId, navPages: Views.Wallets.WalletsNavPages.PSBT);
            await s.Page.Locator("[name='PSBT']").FillAsync(skeletonPSBT.ToBase64());
            await s.Page.ClickAsync("#Decode");
            await s.Page.ClickAsync("#SignTransaction");
            await s.Page.ClickAsync("#SignWithSeed");
            await s.Page.Locator("[name='SeedOrKey']").FillAsync(seed.ToString());
            await s.Page.ClickAsync("#Submit");
            await s.Page.ClickAsync("#BroadcastTransaction");
            await s.FindAlertMessage();

            // Let's check if the update feature works
            await s.GoToWallet(navPages: Views.Wallets.WalletsNavPages.PSBT);
            await s.Page.Locator("[name='PSBT']").FillAsync(skeletonPSBT.ToBase64());
            await s.Page.ClickAsync("#Decode");
            await s.Page.ClickAsync("#PSBTOptionsAdvancedHeader");
            await s.Page.ClickAsync("#update-psbt");

            psbt = await ExtractPSBT(s);
            psbtParsed = PSBT.Parse(psbt, s.Server.NetworkProvider.BTC.NBitcoinNetwork);
            Assert.Single(psbtParsed.Inputs[0].HDKeyPaths);
            Assert.Empty(psbtParsed.Inputs[0].PartialSigs);

            // Let's if we can combine the updated psbt (which has hdkeys, but no sig)
            // with the signed psbt (which has sig, but no hdkeys)
            await s.GoToWallet(s.WalletId, navPages: Views.Wallets.WalletsNavPages.PSBT);
            await s.Page.Locator("[name='PSBT']").FillAsync(psbtParsed.ToBase64());
            await s.Page.ClickAsync("#Decode");
            await s.Page.ClickAsync("#PSBTOptionsAdvancedHeader");
            await s.Page.ClickAsync("#combine-psbt");
            signedPSBT.Inputs[0].HDKeyPaths.Clear();
            await s.Page.Locator("[name='PSBT']").FillAsync(signedPSBT.ToBase64());
            await s.Page.ClickAsync("#Submit");
            psbt = await ExtractPSBT(s);
            psbtParsed = PSBT.Parse(psbt, s.Server.NetworkProvider.BTC.NBitcoinNetwork);
            Assert.Single(psbtParsed.Inputs[0].HDKeyPaths);
            Assert.Single(psbtParsed.Inputs[0].PartialSigs);
        }

        private static async Task SendAllTo(PlaywrightTester s, string address)
        {
            await s.Page.Locator("[name='Outputs[0].DestinationAddress']").FillAsync(address);
            await s.Page.ClickAsync(".crypto-balance-link");
            await s.Page.ClickAsync("#SignTransaction");
        }

        private Task<string> ExtractPSBT(PlaywrightTester s) => s.Page.Locator("#psbt-base64").TextContentAsync();
    }
}
