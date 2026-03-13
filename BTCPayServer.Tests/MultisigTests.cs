using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Plugins.Multisig.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Views.Wallets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using NBitcoin;
using NBitcoin.RPC;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class MultisigTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    [Fact]
    [Trait("Integration", "Integration")]
    public async Task SignTestPSBT()
    {
        var cryptoCode = "BTC";
        using var s = CreateServerTester();
        await s.StartAsync();

        var network = s.NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        var resp1 = generateWalletResp("tprv8ZgxMBicQKsPeGSkDtxjScBmmHP4rfSEPkf1vNmoqt5QjPTco2zPd6UVWkJf2fU8gdKPYRdDMizxtMRqmpVpxsWuqRxVs2d5VsEhwxaK3h7",
            "57b3f43a/84'/1'/0'", "tpubDCzBHRPRcv7Y3utw1hZVrCar21gsj8vsXcehAG4z3R4NnmdMAASQwYYxGBd2f4q5s5ZFGvQBBFs1jVcGsXYoSTA1YFQPwizjsQLU12ibLyu", network);
        var resp2 = generateWalletResp("tprv8ZgxMBicQKsPeC6Xuw83UJHgjnszEUjwH9E5f5FZ3fHgJHBQApo8CmFCsowcdwbRM119UnTqSzVWUsWGtLsxc8wnZa5L8xmEsvEpiyRj4Js",
            "ee7d36c4/84'/1'/0'", "tpubDCetxnEjn8HXA5NrDZbKKTUUYoWCVC2V3X7Kmh3o9UYTfh9c3wTPKyCyeUrLkQ8KHYptEsBoQq6AgqPZiW5neEgb2kjKEr41q1qSevoPFDM", network);
        var resp3 = generateWalletResp("tprv8ZgxMBicQKsPekSniuKwLtXpB82dSDV8ZAK4uLUHxkiHWfDtR5yYwNZiicKdpT3UYwzTTMvXESCm45KyAiH7kiJY6yk51neC9ZvmwDpNsQh",
            "6c014fb3/84'/1'/0'", "tpubDCaTgjJfS5UEim6h66VpQBEZ2Tj6hHk8TzvL81HygdW1M8vZCRhUZLNhb3WTimyP2XMQRA3QGZPwwxUsEFQYK4EoRUWTcb9oB237FJ112tN", network);

        var multisigDerivationScheme = $"wsh(multi(2,[{resp1.AccountKeyPath}]{resp1.DerivationScheme}/0/*," +
                                       $"[{resp2.AccountKeyPath}]{resp2.DerivationScheme}/0/*," +
                                       $"[{resp3.AccountKeyPath}]{resp3.DerivationScheme}/0/*))";

        var strategy = UIStoreOnChainWalletsController.ParseDerivationStrategy(multisigDerivationScheme, network);
        strategy.Source = "ManualDerivationScheme";
        var derivationScheme = strategy.AccountDerivation;

        var testPSBT =
            "cHNidP8BAIkCAAAAAQmiSunnaKN7F4Jv5uHROfYbIZOckCck/Wo7gAQmi9hfAAAAAAD9////AtgbZgAAAAAAIgAgWCUFlU9eWkyxn0l0yQxs2rXQZ7d9Ry8LaYECaVC0TUGAlpgAAAAAACIAIFZxT+UIdhHZC4qFPhPQ6IXdX+44HIxCYcoh/bNOhB0hAAAAAAABAStAAf8AAAAAACIAIL2DDkfKwKHxZj2EKxXUd4uwf0IvPaCxUtAPq9snpq9TAQDqAgAAAAABAVuHuou9E5y6zUJaUreQD0wUeiPnT2aY+YU7QaPJOiQCAAAAAAD9////AkAB/wAAAAAAIgAgvYMOR8rAofFmPYQrFdR3i7B/Qi89oLFS0A+r2yemr1PM5AYpAQAAABYAFIlFupZkD07+GRo24WRS3IFcf+EuAkcwRAIgGi9wAcTfc0d0+j+Vg82aYklXCUsPg+g3jS+PTBTSQwkCIAPh5CZF18DTBKqWU2qdhNCbZ8Tp/NCEHjLJRHcH0oluASECWnI1s9ozQRL2qbK6JbLHzj9LlU9Pras3nZfq/njBJwhwAAAAAQVpUiECMCCasr2FRmRMiWkM/l1iraFR18td5SZ2APyQiaI0yY8hA8K96vH64BelUJiEPGwM6UTwRSfAJUR2j8dkw7i31fFTIQMlHLlaAPxw3fl1vaM1EofIirt79MXOryM54zpHwu1GlVOuIgIDwr3q8frgF6VQmIQ8bAzpRPBFJ8AlRHaPx2TDuLfV8VNHMEQCIANnprskJz8oVsetqOEViHtzhmSG8c36r3zmUIHwIoOhAiAZ1jBqj40iu2S/nMfiGyuCC/jSiSGik7YVwiwN+bbxPAEiBgIwIJqyvYVGZEyJaQz+XWKtoVHXy13lJnYA/JCJojTJjxhXs/Q6VAAAgAEAAIAAAACAAAAAAAUAAAAiBgMlHLlaAPxw3fl1vaM1EofIirt79MXOryM54zpHwu1GlRhsAU+zVAAAgAEAAIAAAACAAAAAAAUAAAAiBgPCverx+uAXpVCYhDxsDOlE8EUnwCVEdo/HZMO4t9XxUxjufTbEVAAAgAEAAIAAAACAAAAAAAUAAAAAAQFpUiEDa/J6SaiRjP1jhq9jpNxFKovEuWBz28seNMvsn0JC/ZIhA7p3bS7vLYB5UxlNN6YqkEDITyaMlk/i450q6+4woveAIQPTchIOrd+TNGBOX6il1HRZnBndyRoUj/hahbjTaAGHglOuIgIDa/J6SaiRjP1jhq9jpNxFKovEuWBz28seNMvsn0JC/ZIYV7P0OlQAAIABAACAAAAAgAEAAAABAAAAIgIDundtLu8tgHlTGU03piqQQMhPJoyWT+LjnSrr7jCi94AY7n02xFQAAIABAACAAAAAgAEAAAABAAAAIgID03ISDq3fkzRgTl+opdR0WZwZ3ckaFI/4WoW402gBh4IYbAFPs1QAAIABAACAAAAAgAEAAAABAAAAAAEBaVIhA/fCRR3MWwCgNuXMvlWLonY+TurUKOHXOSHALCck62deIQPqeQXD8ws9SDEDXSyD6a3WFlIGH+gDUf2/xAfw8HxE8iEC3LBRJYYxRzIeg9NxLGvtfATvFaKsO9D7AUjoTLZzke5TriICAtywUSWGMUcyHoPTcSxr7XwE7xWirDvQ+wFI6Ey2c5HuGGwBT7NUAACAAQAAgAAAAIAAAAAADAAAACICA+p5BcPzCz1IMQNdLIPprdYWUgYf6ANR/b/EB/DwfETyGO59NsRUAACAAQAAgAAAAIAAAAAADAAAACICA/fCRR3MWwCgNuXMvlWLonY+TurUKOHXOSHALCck62deGFez9DpUAACAAQAAgAAAAIAAAAAADAAAAAA=";

        var signedPsbt = SignWithSeed(testPSBT, derivationScheme, resp1);
        s.TestLogs.LogInformation($"Signed PSBT: {signedPsbt}");
    }

    [Fact]
    [Trait("Playwright", "Playwright-2")]
    public async Task CanEnableAndUseMultisigWallet()
    {
        var cryptoCode = "BTC";
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        var owner = await s.RegisterNewUser(true);

        var network = s.Server.NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        var resp1 = generateWalletResp("tprv8ZgxMBicQKsPeGSkDtxjScBmmHP4rfSEPkf1vNmoqt5QjPTco2zPd6UVWkJf2fU8gdKPYRdDMizxtMRqmpVpxsWuqRxVs2d5VsEhwxaK3h7",
            "57b3f43a/84'/1'/0'", "tpubDCzBHRPRcv7Y3utw1hZVrCar21gsj8vsXcehAG4z3R4NnmdMAASQwYYxGBd2f4q5s5ZFGvQBBFs1jVcGsXYoSTA1YFQPwizjsQLU12ibLyu", network);
        var resp2 = generateWalletResp("tprv8ZgxMBicQKsPeC6Xuw83UJHgjnszEUjwH9E5f5FZ3fHgJHBQApo8CmFCsowcdwbRM119UnTqSzVWUsWGtLsxc8wnZa5L8xmEsvEpiyRj4Js",
            "ee7d36c4/84'/1'/0'", "tpubDCetxnEjn8HXA5NrDZbKKTUUYoWCVC2V3X7Kmh3o9UYTfh9c3wTPKyCyeUrLkQ8KHYptEsBoQq6AgqPZiW5neEgb2kjKEr41q1qSevoPFDM", network);
        var resp3 = generateWalletResp("tprv8ZgxMBicQKsPekSniuKwLtXpB82dSDV8ZAK4uLUHxkiHWfDtR5yYwNZiicKdpT3UYwzTTMvXESCm45KyAiH7kiJY6yk51neC9ZvmwDpNsQh",
            "6c014fb3/84'/1'/0'", "tpubDCaTgjJfS5UEim6h66VpQBEZ2Tj6hHk8TzvL81HygdW1M8vZCRhUZLNhb3WTimyP2XMQRA3QGZPwwxUsEFQYK4EoRUWTcb9oB237FJ112tN", network);

        var multisigDerivationScheme = $"wsh(multi(2,[{resp1.AccountKeyPath}]{resp1.DerivationScheme}/0/*," +
                                       $"[{resp2.AccountKeyPath}]{resp2.DerivationScheme}/0/*," +
                                       $"[{resp3.AccountKeyPath}]{resp3.DerivationScheme}/0/*))";

        var strategy = UIStoreOnChainWalletsController.ParseDerivationStrategy(multisigDerivationScheme, network);
        strategy.Source = "ManualDerivationScheme";
        var derivationScheme = strategy.AccountDerivation;
        await s.CreateNewStore();
        var storeId = s.StoreId;
        await s.ConfigureServerEmailWithMailPit(from: "multisig@test.com", login: "multisig@test.com", password: "multisig@test.com");
        await s.Logout();
        await s.GoToRegister();
        var multisigner = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToLogin();
        await s.LogIn(owner);
        await using (var scope = s.Server.PayTester.GetService<IServiceScopeFactory>().CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var storeRepo = scope.ServiceProvider.GetRequiredService<StoreRepository>();
            var multisignerUser = await userManager.FindByEmailAsync(multisigner);
            Assert.NotNull(multisignerUser);
            await storeRepo.AddOrUpdateStoreUser(storeId, multisignerUser.Id, new StoreRoleId("Multisigner"));
        }
        await s.GoToWalletSettings();
        await s.Page.ClickAsync("#ImportWalletOptionsLink");
        await s.Page.ClickAsync("#ImportXpubLink");
        await s.Page.FillAsync("#DerivationScheme", multisigDerivationScheme);
        await s.Page.ClickAsync("#Continue");
        await s.Page.ClickAsync("#Confirm");
        s.TestLogs.LogInformation($"Multisig wallet setup: {multisigDerivationScheme}");

        // fetch address from receive page
        await s.GoToWallet(navPages: WalletsNavPages.Receive);

        var addressElement = s.Page.Locator("#Address");
        await addressElement.ClickAsync();
        var address = await addressElement.GetAttributeAsync("data-text");
        Assert.NotNull(address);
        await s.Page.ClickAsync("//button[@value='fill-wallet']");
        await s.Page.ClickAsync("#CancelWizard");

        // we are creating a pending transaction
        await s.GoToWallet(navPages: WalletsNavPages.Send);
        await s.Page.FillAsync("#Outputs_0__DestinationAddress", address);
        var amount = "0.1";
        await s.Page.FillAsync("#Outputs_0__Amount", amount);
        var pendingCreatedEmail = await s.Server.AssertHasEmail(async () =>
        {
            await s.Page.ClickAsync("#CreatePendingTransaction");
        });
        Assert.Equal("Pending multisig transaction requires signatures (BTC)", pendingCreatedEmail.Subject);

        // validating the state of UI
        Assert.Equal("0", await s.Page.TextContentAsync("#Sigs_0__Collected"));
        Assert.Equal("2/3", await s.Page.TextContentAsync("#Sigs_0__Scheme"));

        // now proceeding to click on sign button and sign transactions
        await SignPendingTransactionWithKey(s, address, derivationScheme, resp1, "Multisig signature collected (BTC)");
        Assert.Equal("1", await s.Page.TextContentAsync("#Sigs_0__Collected"));

        await SignPendingTransactionWithKey(s, address, derivationScheme, resp2);
        Assert.Equal("2", await s.Page.TextContentAsync("#Sigs_0__Collected"));

        // we should now have enough signatures to broadcast transaction
        await s.Page.ClickAsync("//a[text()='Broadcast']");
        await s.Page.ClickAsync("#BroadcastTransaction");
        await s.FindAlertMessage(partialText: "Transaction broadcasted successfully");

        // now that we broadcast transaction, there shouldn't be broadcast button
        Assert.False(await s.Page.Locator("//a[text()='Broadcast']").IsVisibleAsync());

        // Abort pending transaction flow
        await s.GoToWallet(navPages: WalletsNavPages.Send);
        await s.Page.FillAsync("#Outputs_0__DestinationAddress", address);
        await s.Page.FillAsync("#Outputs_0__Amount", "0.2");
        await s.Page.ClickAsync("#CreatePendingTransaction");

        await s.Page.ClickAsync("//a[text()='Abort']");

        await s.Page.ClickAsync("#ConfirmContinue");
        await s.FindAlertMessage(partialText: "Aborted Pending Transaction");

        // Collecting without a user id should still notify via the pending transaction event.
        await s.GoToWallet(navPages: WalletsNavPages.Send);
        await s.Page.FillAsync("#Outputs_0__DestinationAddress", address);
        await s.Page.FillAsync("#Outputs_0__Amount", "0.3");
        var pendingCreatedWithoutSignerEmail = await s.Server.AssertHasEmail(async () =>
        {
            await s.Page.ClickAsync("#CreatePendingTransaction");
        });
        Assert.Equal("Pending multisig transaction requires signatures (BTC)", pendingCreatedWithoutSignerEmail.Subject);

        var pendingTransactionService = s.Server.PayTester.GetService<PendingTransactionService>();
        var pendingWithoutSigner = Assert.Single(await pendingTransactionService.GetPendingTransactions(cryptoCode, storeId));
        var pendingWithoutSignerBlob = pendingWithoutSigner.GetBlob();
        Assert.NotNull(pendingWithoutSignerBlob?.PSBT);
        var signedWithoutSigner = SignWithSeed(pendingWithoutSignerBlob.PSBT, derivationScheme, resp1);
        var signatureCollectedWithoutSignerEmail = await s.Server.AssertHasEmail(async () =>
        {
            await pendingTransactionService.CollectSignature(
                new PendingTransactionService.PendingTransactionFullId(cryptoCode, storeId, pendingWithoutSigner.Id),
                PSBT.Parse(signedWithoutSigner, network.NBitcoinNetwork),
                CancellationToken.None,
                signerUserId: null);
        });
        Assert.Equal("Multisig signature collected (BTC)", signatureCollectedWithoutSignerEmail.Subject);

        s.TestLogs.LogInformation($"Finished MultiSig Flow");
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanUseMultisigSetup()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        await s.StartAsync();

        var owner = await s.RegisterNewUser(true);
        await s.SkipWizard();
        var (_, storeId) = await s.CreateNewStore();
        await s.ConfigureServerEmailWithMailPit(from: "multisig-setup@test.com", login: "multisig-setup@test.com", password: "multisig-setup@test.com");
        await s.Logout();
        await s.GoToRegister();
        var signerA = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        var signerB = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        var walletManager = await s.RegisterNewUser();
        await s.SkipWizard();

        await using var scope = s.Server.PayTester.GetService<IServiceScopeFactory>().CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var storeRepo = scope.ServiceProvider.GetRequiredService<StoreRepository>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("MultisigInviteLink");

        var signerAUser = await userManager.FindByEmailAsync(signerA);
        var signerBUser = await userManager.FindByEmailAsync(signerB);
        var walletManagerUser = await userManager.FindByEmailAsync(walletManager);
        Assert.NotNull(signerAUser);
        Assert.NotNull(signerBUser);
        Assert.NotNull(walletManagerUser);

        await storeRepo.AddOrUpdateStoreUser(storeId, signerAUser.Id, new StoreRoleId("Multisigner Guest"));
        await storeRepo.AddOrUpdateStoreUser(storeId, signerBUser.Id, new StoreRoleId("Multisigner Guest"));
        await storeRepo.AddOrUpdateStoreUser(storeId, walletManagerUser.Id, new StoreRoleId("Wallet Manager"));

        async Task<string> CreateRequest(params string[] participantEmails)
        {
            await s.GoToHome();
            await s.Logout();
            await s.GoToLogin();
            await s.LogIn(owner);
            await s.GoToUrl($"/stores/{storeId}/onchain/BTC/import/multisig");
            foreach (var participantEmail in participantEmails)
            {
                await s.Page.Locator($"label.multisig-signer-item:has-text('{participantEmail}') input[type='checkbox']").CheckAsync();
            }
            await s.Page.FillAsync("#MultisigRequiredSigners", "2");
            await s.Page.FillAsync("#MultisigTotalSigners", "2");
            await s.Page.SelectOptionAsync("#MultisigScriptType", "p2wsh");
            var inviteEmail = await s.Server.AssertHasEmail(async () =>
            {
                await s.Page.ClickAsync("button[value='create-request']");
            });
            Assert.Equal("Multisig signer request for BTC", inviteEmail.Subject);
            Assert.Contains("multisig/invite/", inviteEmail.Html ?? inviteEmail.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            await s.FindAlertMessage(partialText: "Multisig signer requests were created.");
            var currentPending = await storeRepo.GetSettingAsync<PendingMultisigSetupData>(storeId, "PendingMultisigSetup-BTC");
            Assert.NotNull(currentPending);
            Assert.Equal(2, currentPending.Participants.Count);
            Assert.All(currentPending.Participants, p => Assert.True(string.IsNullOrEmpty(p.AccountKey)));
            return currentPending.RequestId;
        }

        async Task SubmitSigner(string email, string requestId, string userId, string accountKey, string fingerprint, string accountKeyPath, string expectedMessage, bool submitForm = true, bool useVaultCallback = false, bool expectNotification = true)
        {
            await s.GoToHome();
            await s.Logout();
            await s.GoToLogin();
            await s.LogIn(email);
            var expires = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();
            var protectedToken = protector.Protect($"{storeId}|BTC|{requestId}|{userId}|{expires}");
            var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedToken));
            if (useVaultCallback)
            {
                await InstallMockVaultAsync(s.Page, accountKey, fingerprint);
            }
            await s.GoToUrl($"/stores/{storeId}/onchain/BTC/multisig/invite/{Uri.EscapeDataString(token)}");
            if (submitForm)
            {
                if (useVaultCallback)
                {
                    await Expect(s.Page.Locator("#SubmitSignerKeyButton")).ToBeDisabledAsync();
                    await Expect(s.Page.Locator("#SignerKeyFields")).ToHaveClassAsync(new Regex("d-none"));
                    await Expect(s.Page.Locator("#vault-confirm")).ToBeVisibleAsync();
                    await s.Page.ClickAsync("#vault-confirm");
                    await Expect(s.Page.Locator("#SignerKeyFields")).Not.ToHaveClassAsync(new Regex("d-none"));
                    await Expect(s.Page.Locator("#SubmitSignerKeyButton")).ToBeEnabledAsync();
                    Assert.Equal(accountKey, await s.Page.InputValueAsync("#AccountKey"));
                    Assert.Equal(fingerprint, await s.Page.InputValueAsync("#MasterFingerprint"));
                    Assert.Equal(accountKeyPath, await s.Page.InputValueAsync("#AccountKeyPath"));
                }
                else
                {
                    await s.Page.ClickAsync("#EntryModeManual");
                    await s.Page.FillAsync("#AccountKey", accountKey);
                    await s.Page.FillAsync("#MasterFingerprint", fingerprint);
                    await s.Page.FillAsync("#AccountKeyPath", accountKeyPath);
                }
                if (expectNotification)
                {
                    var submissionEmail = await s.Server.AssertHasEmail(async () =>
                    {
                        await s.Page.ClickAsync("button[type='submit']");
                    });
                    Assert.Equal("Multisig signer submitted (BTC)", submissionEmail.Subject);
                }
                else
                {
                    await s.Page.ClickAsync("button[type='submit']");
                }
            }
            await Expect(s.Page.Locator("body")).ToContainTextAsync(expectedMessage);
        }

        var signerAKey = CreateTestMultisigSignerKey("all all all all all all all all all all all all", "48'/1'/0'/2'");
        var signerBKey = CreateTestMultisigSignerKey("click chunk owner kingdom faint steak safe evidence bicycle repeat bulb wheel");
        var walletManagerKey = CreateTestMultisigSignerKey("letter advice cage absurd amount doctor acoustic avoid letter advice cage above");

        var firstRequestId = await CreateRequest(signerA, signerB);
        var expires = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();
        var signerBProtectedToken = protector.Protect($"{storeId}|BTC|{firstRequestId}|{signerBUser.Id}|{expires}");
        var signerBToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(signerBProtectedToken));
        var signerBInvitePath = $"/stores/{storeId}/onchain/BTC/multisig/invite/{Uri.EscapeDataString(signerBToken)}";

        await s.Page.ClickAsync("a.cancel");
        await s.Page.Locator("#menu-item-Account").WaitForAsync();
        await s.Logout();
        await s.GoToLogin();
        await s.LogIn(signerA);
        await s.GoToUrl(signerBInvitePath);
        var redirectedToLogin = new Uri(s.Page.Url);
        Assert.Contains("/login", redirectedToLogin.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        var loginQuery = QueryHelpers.ParseQuery(redirectedToLogin.Query);
        Assert.True(loginQuery.TryGetValue("ReturnUrl", out var returnUrl) && returnUrl.Count > 0);
        Assert.Equal(signerBInvitePath, returnUrl[0]);
        var redirectedBody = await s.Page.TextContentAsync("body") ?? string.Empty;
        Assert.DoesNotContain("404", redirectedBody, StringComparison.OrdinalIgnoreCase);

        await SubmitSigner(signerA, firstRequestId, signerAUser.Id, signerAKey.AccountKey, signerAKey.MasterFingerprint, signerAKey.AccountKeyPath, "Your signer key is submitted.", useVaultCallback: true);
        var pendingAfterFirstSubmit = await storeRepo.GetSettingAsync<PendingMultisigSetupData>(storeId, "PendingMultisigSetup-BTC");
        var signerAParticipant = pendingAfterFirstSubmit.Participants.Single(p => p.UserId == signerAUser.Id);
        Assert.Equal(signerAKey.AccountKey, signerAParticipant.AccountKey);
        Assert.NotNull(signerAParticipant.SubmittedAt);
        var signerAFirstSubmittedAt = signerAParticipant.SubmittedAt;

        await SubmitSigner(signerA, firstRequestId, signerAUser.Id, signerBKey.AccountKey, signerBKey.MasterFingerprint, signerBKey.AccountKeyPath, "Your signer key is submitted.", submitForm: false);
        var pendingAfterResubmit = await storeRepo.GetSettingAsync<PendingMultisigSetupData>(storeId, "PendingMultisigSetup-BTC");
        var signerAAfterResubmit = pendingAfterResubmit.Participants.Single(p => p.UserId == signerAUser.Id);
        Assert.Equal(signerAKey.AccountKey, signerAAfterResubmit.AccountKey);
        Assert.Equal(signerAFirstSubmittedAt, signerAAfterResubmit.SubmittedAt);

        await SubmitSigner(signerB, firstRequestId, signerBUser.Id, signerAKey.AccountKey, signerAKey.MasterFingerprint, signerAKey.AccountKeyPath, "This signer key is already used in this multisig request.", expectNotification: false);
        var pendingAfterDuplicate = await storeRepo.GetSettingAsync<PendingMultisigSetupData>(storeId, "PendingMultisigSetup-BTC");
        var signerBParticipant = pendingAfterDuplicate.Participants.Single(p => p.UserId == signerBUser.Id);
        Assert.True(string.IsNullOrWhiteSpace(signerBParticipant.AccountKey));
        Assert.Null(signerBParticipant.SubmittedAt);

        await s.GoToHome();
        await s.Logout();
        await s.GoToLogin();
        await s.LogIn(owner);
        await s.GoToUrl($"/stores/{storeId}/onchain/BTC/import/multisig?MultisigRequestId={firstRequestId}");
        await Expect(s.Page.Locator("button[value='finalize-request']")).ToBeDisabledAsync();

        var pendingForLegacyFinalize = await storeRepo.GetSettingAsync<PendingMultisigSetupData>(storeId, "PendingMultisigSetup-BTC");
        var signerBForLegacyFinalize = pendingForLegacyFinalize.Participants.Single(p => p.UserId == signerBUser.Id);
        signerBForLegacyFinalize.AccountKey = signerAKey.AccountKey;
        signerBForLegacyFinalize.MasterFingerprint = signerAKey.MasterFingerprint;
        signerBForLegacyFinalize.AccountKeyPath = signerAKey.AccountKeyPath;
        signerBForLegacyFinalize.SubmittedAt = DateTimeOffset.UtcNow;
        await storeRepo.UpdateSetting(storeId, "PendingMultisigSetup-BTC", pendingForLegacyFinalize);

        await s.GoToUrl($"/stores/{storeId}/onchain/BTC/import/multisig?MultisigRequestId={firstRequestId}");
        await Expect(s.Page.Locator("button[value='finalize-request']")).ToBeEnabledAsync();
        await s.Page.ClickAsync("button[value='finalize-request']");
        await Expect(s.Page.Locator("body")).ToContainTextAsync("Signer keys must be unique.");

        await storeRepo.UpdateSetting<PendingMultisigSetupData>(storeId, "PendingMultisigSetup-BTC", null);

        var secondRequestId = await CreateRequest(walletManager, signerB);
        await SubmitSigner(walletManager, secondRequestId, walletManagerUser.Id, walletManagerKey.AccountKey, walletManagerKey.MasterFingerprint, walletManagerKey.AccountKeyPath, "Your signer key is submitted.");
        await SubmitSigner(signerB, secondRequestId, signerBUser.Id, signerBKey.AccountKey, signerBKey.MasterFingerprint, signerBKey.AccountKeyPath, "Your signer key is submitted.");

        await s.GoToHome();
        await s.Logout();
        await s.GoToLogin();
        await s.LogIn(walletManager);
        await s.GoToUrl("/wallets");
        var multisigRow = s.Page.Locator("#Wallets-MultisigInProgress tbody tr").First;
        await Expect(multisigRow).ToContainTextAsync("2/2 submitted");
        await Expect(multisigRow).ToContainTextAsync("Create wallet");
        await multisigRow.Locator("a").ClickAsync();
        await Expect(s.Page.Locator("button[value='finalize-request']")).ToBeEnabledAsync();
        await s.Page.ClickAsync("button[value='finalize-request']");
        await Expect(s.Page.Locator("#Confirm")).ToBeVisibleAsync();
        var walletCreatedEmail = await s.Server.AssertHasEmail(async () =>
        {
            await s.Page.ClickAsync("#Confirm");
        });
        Assert.Equal("Multisig wallet created for BTC", walletCreatedEmail.Subject);
    }

    private async Task SignPendingTransactionWithKey(PlaywrightTester s, string address,
        DerivationStrategyBase derivationScheme, GenerateWalletResponse signingKey, string expectedNotificationSubject = null)
    {
        // getting to pending transaction page
        await s.Page.ClickAsync("//a[text()='View']");
        await s.Page.Locator($"//tr[td[text()='{address}']]").WaitForAsync();

        await s.Page.Locator("#SignTransaction").WaitForAsync();

        // fetching PSBT
        await s.Page.ClickAsync("#PSBTOptionsExportHeader");
        await s.Page.ClickAsync("#ShowRawVersion");

        var psbt = await s.Page.Locator("#psbt-base64").TextContentAsync();

        // signing PSBT and entering it to submit
        var signedPsbt = SignWithSeed(psbt, derivationScheme, signingKey);

        await s.Page.ClickAsync("#PSBTOptionsImportHeader");
        await s.Page.FillAsync("#ImportedPSBT", signedPsbt);

        if (string.IsNullOrEmpty(expectedNotificationSubject))
            await s.Page.ClickAsync("#Collect");
        else
        {
            var signatureCollectedEmail = await s.Server.AssertHasEmail(async () =>
            {
                await s.Page.ClickAsync("#Collect");
            });
            Assert.Equal(expectedNotificationSubject, signatureCollectedEmail.Subject);
        }
    }

    private GenerateWalletResponse generateWalletResp(string tpriv, string keypath, string derivation, BTCPayNetwork network)
    {
        var key1 = new BitcoinExtKey(
                ExtKey.Parse(tpriv, Network.RegTest),
            Network.RegTest);


        var parser = new DerivationSchemeParser(network);

        var resp1 = new GenerateWalletResponse
        {
            MasterHDKey = key1,
            DerivationScheme = (StandardDerivationStrategyBase)parser.Parse(derivation),
            AccountKeyPath = RootedKeyPath.Parse(keypath)
        };
        return resp1;
    }

    private static async Task InstallMockVaultAsync(IPage page, string accountKey, string fingerprint)
    {
        await page.Context.AddInitScriptAsync($$"""
            (() => {
                const originalFetch = window.fetch.bind(window);
                const accountKey = {{System.Text.Json.JsonSerializer.Serialize(accountKey)}};
                const fingerprint = {{System.Text.Json.JsonSerializer.Serialize(fingerprint)}};
                window.__vaultRequests = [];
                window.fetch = async (input, init) => {
                    const url = typeof input === 'string' ? input : input.url;
                    if (!url.startsWith('http://127.0.0.1:65092/hwi-bridge/v1')) {
                        return originalFetch(input, init);
                    }

                    if (url.endsWith('/request-permission')) {
                        return new Response('', { status: 200, headers: { 'Content-Type': 'text/plain' } });
                    }

                    const requestBody = init && typeof init.body === 'string' ? JSON.parse(init.body) : {};
                    const params = Array.isArray(requestBody.params) ? requestBody.params : [];
                    const command = params.join(' ');
                    window.__vaultRequests.push(command);

                    if (command.includes('--version')) {
                        return new Response('hwi 2.4.0', { status: 200, headers: { 'Content-Type': 'text/plain' } });
                    }

                    if (command.includes('enumerate')) {
                        return new Response(JSON.stringify([{
                            model: 'trezor_safe_3',
                            path: 'mock-device-path',
                            fingerprint,
                            needs_pin_sent: false,
                            needs_passphrase_sent: false
                        }]), { status: 200, headers: { 'Content-Type': 'text/plain' } });
                    }

                    if (command.includes('getxpub')) {
                        return new Response(JSON.stringify({ xpub: accountKey }), { status: 200, headers: { 'Content-Type': 'text/plain' } });
                    }

                    return new Response(JSON.stringify({ success: false, error: 'unexpected command: ' + command }), { status: 200, headers: { 'Content-Type': 'text/plain' } });
                };
            })();
            """);
    }

    private static (string AccountKey, string MasterFingerprint, string AccountKeyPath) CreateTestMultisigSignerKey(string mnemonic, string accountKeyPath = "84'/1'/0'")
    {
        var rootKey = new Mnemonic(mnemonic).DeriveExtKey();
        var path = KeyPath.Parse(accountKeyPath);
        return (
            rootKey.Derive(path).Neuter().ToString(Network.RegTest),
            rootKey.GetPublicKey().GetHDFingerPrint().ToString(),
            $"m/{path}");
    }


    public string SignWithSeed(string psbtBase64, DerivationStrategyBase derivationStrategyBase,
        GenerateWalletResponse resp)
    {
        var strMasterHdKey = resp.MasterHDKey;
        var extKey = new BitcoinExtKey(strMasterHdKey, Network.RegTest);

        var strKeypath = resp.AccountKeyPath.ToStringWithEmptyKeyPathAware();
        RootedKeyPath rootedKeyPath = RootedKeyPath.Parse(strKeypath);


        if (rootedKeyPath.MasterFingerprint != extKey.GetPublicKey().GetHDFingerPrint())
            throw new Exception("Master fingerprint mismatch. Ensure the wallet matches the PSBT.");
        // finished setting variables, now onto signing

        var psbt = PSBT.Parse(psbtBase64, Network.RegTest);

        // Sign the PSBT
        extKey = extKey.Derive(rootedKeyPath.KeyPath);
        psbt.Settings.SigningOptions = new SigningOptions();
        var changed = psbt.PSBTChanged(() => psbt.SignAll(derivationStrategyBase, extKey, rootedKeyPath));

        if (!changed)
            throw new Exception("Failed to sign the PSBT. Ensure the inputs align with the account key path.");

        // Return the updated and signed PSBT
        return psbt.ToBase64();
    }
}
