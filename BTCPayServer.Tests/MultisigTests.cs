using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.Multisig.Models;
using BTCPayServer.Plugins.Multisig.Services;
using BTCPayServer.Plugins.Wallets;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using BTCPayServer.Views.Wallets;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class MultisigTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    [Fact]
    [Trait("Integration", "Integration")]
    public Task SignTestPSBT()
    {
        const string cryptoCode = "BTC";
        var network = CreateNetworkProvider().GetNetwork<BTCPayNetwork>(cryptoCode);
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

        const string testPSBT = "cHNidP8BAIkCAAAAAQmiSunnaKN7F4Jv5uHROfYbIZOckCck/Wo7gAQmi9hfAAAAAAD9////AtgbZgAAAAAAIgAgWCUFlU9eWkyxn0l0yQxs2rXQZ7d9Ry8LaYECaVC0TUGAlpgAAAAAACIAIFZxT+UIdhHZC4qFPhPQ6IXdX+44HIxCYcoh/bNOhB0hAAAAAAABAStAAf8AAAAAACIAIL2DDkfKwKHxZj2EKxXUd4uwf0IvPaCxUtAPq9snpq9TAQDqAgAAAAABAVuHuou9E5y6zUJaUreQD0wUeiPnT2aY+YU7QaPJOiQCAAAAAAD9////AkAB/wAAAAAAIgAgvYMOR8rAofFmPYQrFdR3i7B/Qi89oLFS0A+r2yemr1PM5AYpAQAAABYAFIlFupZkD07+GRo24WRS3IFcf+EuAkcwRAIgGi9wAcTfc0d0+j+Vg82aYklXCUsPg+g3jS+PTBTSQwkCIAPh5CZF18DTBKqWU2qdhNCbZ8Tp/NCEHjLJRHcH0oluASECWnI1s9ozQRL2qbK6JbLHzj9LlU9Pras3nZfq/njBJwhwAAAAAQVpUiECMCCasr2FRmRMiWkM/l1iraFR18td5SZ2APyQiaI0yY8hA8K96vH64BelUJiEPGwM6UTwRSfAJUR2j8dkw7i31fFTIQMlHLlaAPxw3fl1vaM1EofIirt79MXOryM54zpHwu1GlVOuIgIDwr3q8frgF6VQmIQ8bAzpRPBFJ8AlRHaPx2TDuLfV8VNHMEQCIANnprskJz8oVsetqOEViHtzhmSG8c36r3zmUIHwIoOhAiAZ1jBqj40iu2S/nMfiGyuCC/jSiSGik7YVwiwN+bbxPAEiBgIwIJqyvYVGZEyJaQz+XWKtoVHXy13lJnYA/JCJojTJjxhXs/Q6VAAAgAEAAIAAAACAAAAAAAUAAAAiBgMlHLlaAPxw3fl1vaM1EofIirt79MXOryM54zpHwu1GlRhsAU+zVAAAgAEAAIAAAACAAAAAAAUAAAAiBgPCverx+uAXpVCYhDxsDOlE8EUnwCVEdo/HZMO4t9XxUxjufTbEVAAAgAEAAIAAAACAAAAAAAUAAAAAAQFpUiEDa/J6SaiRjP1jhq9jpNxFKovEuWBz28seNMvsn0JC/ZIhA7p3bS7vLYB5UxlNN6YqkEDITyaMlk/i450q6+4woveAIQPTchIOrd+TNGBOX6il1HRZnBndyRoUj/hahbjTaAGHglOuIgIDa/J6SaiRjP1jhq9jpNxFKovEuWBz28seNMvsn0JC/ZIYV7P0OlQAAIABAACAAAAAgAEAAAABAAAAIgIDundtLu8tgHlTGU03piqQQMhPJoyWT+LjnSrr7jCi94AY7n02xFQAAIABAACAAAAAgAEAAAABAAAAIgID03ISDq3fkzRgTl+opdR0WZwZ3ckaFI/4WoW402gBh4IYbAFPs1QAAIABAACAAAAAgAEAAAABAAAAAAEBaVIhA/fCRR3MWwCgNuXMvlWLonY+TurUKOHXOSHALCck62deIQPqeQXD8ws9SDEDXSyD6a3WFlIGH+gDUf2/xAfw8HxE8iEC3LBRJYYxRzIeg9NxLGvtfATvFaKsO9D7AUjoTLZzke5TriICAtywUSWGMUcyHoPTcSxr7XwE7xWirDvQ+wFI6Ey2c5HuGGwBT7NUAACAAQAAgAAAAIAAAAAADAAAACICA+p5BcPzCz1IMQNdLIPprdYWUgYf6ANR/b/EB/DwfETyGO59NsRUAACAAQAAgAAAAIAAAAAADAAAACICA/fCRR3MWwCgNuXMvlWLonY+TurUKOHXOSHALCck62deGFez9DpUAACAAQAAgAAAAIAAAAAADAAAAAA=";

        var signedPsbt = SignWithSeed(testPSBT, derivationScheme, resp1);
        TestLogs.LogInformation($"Signed PSBT: {signedPsbt}");
        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Integration", "Integration")]
    public async Task PendingMultisigTransactionTracksPerInputProgress()
    {
        var dbTester = CreateDBTester();
        await dbTester.MigrateAsync();
        var contextFactory = dbTester.CreateContextFactory();
        var storeId = await CreateTestStore(contextFactory);
        var eventAggregator = new EventAggregator(BTCPayLogs);
        var signatureCollectedEvents = 0;
        using var subscription = eventAggregator.Subscribe<PendingTransactionService.PendingTransactionEvent>((_, evt) =>
        {
            if (evt.Type == PendingTransactionService.PendingTransactionEvent.SignatureCollected)
                signatureCollectedEvents++;
        });
        var pendingTransactionService = new PendingTransactionService(
            CreateNetworkProvider(),
            dbTester.CreateContextFactory(),
            eventAggregator,
            LoggerFactory.CreateLogger<PendingTransactionService>());
        var requestBaseUrl = RequestBaseUrl.FromUrl("https://example.com");
        var testPsbt = CreatePendingMultisigPsbt();

        var pendingTransaction = await pendingTransactionService.CreatePendingTransaction(
            storeId,
            "BTC",
            testPsbt.BasePsbt,
            requestBaseUrl,
            cancellationToken: CancellationToken.None);
        var blob = pendingTransaction.GetBlob();
        Assert.NotNull(blob);
        Assert.Equal(2, blob.SignaturesNeeded);
        Assert.Equal(3, blob.SignaturesTotal);
        Assert.Equal(0, blob.SignaturesCollected);

        var signerAAllInputs = SignInputs(testPsbt.BasePsbt, testPsbt.SignerA, 0, 1);
        pendingTransaction = await pendingTransactionService.CollectSignature(
            new PendingTransactionService.PendingTransactionFullId("BTC", storeId, pendingTransaction.Id),
            signerAAllInputs,
            CancellationToken.None);
        Assert.NotNull(pendingTransaction);
        blob = pendingTransaction.GetBlob();
        Assert.Equal(1, blob.SignaturesCollected);
        Assert.Equal(1, Math.Max(0, (blob.SignaturesNeeded ?? 0) - (blob.SignaturesCollected ?? 0)));
        Assert.Equal(PendingTransactionState.Pending, pendingTransaction.State);
        Assert.Single(blob.CollectedSignatures);
        Assert.Equal(1, signatureCollectedEvents);

        pendingTransaction = await pendingTransactionService.CollectSignature(
            new PendingTransactionService.PendingTransactionFullId("BTC", storeId, pendingTransaction.Id),
            signerAAllInputs,
            CancellationToken.None);
        blob = pendingTransaction!.GetBlob();
        Assert.Single(blob.CollectedSignatures);
        Assert.Equal(1, blob.SignaturesCollected);
        Assert.Equal(PendingTransactionState.Pending, pendingTransaction.State);
        Assert.Equal(1, signatureCollectedEvents);

        var signerAPartial = SignInputs(testPsbt.BasePsbt, testPsbt.SignerA, 0);
        pendingTransaction = await pendingTransactionService.CollectSignature(
            new PendingTransactionService.PendingTransactionFullId("BTC", storeId, pendingTransaction.Id),
            signerAPartial,
            CancellationToken.None);
        blob = pendingTransaction!.GetBlob();
        Assert.Single(blob.CollectedSignatures);
        Assert.Equal(1, blob.SignaturesCollected);
        Assert.Equal(PendingTransactionState.Pending, pendingTransaction.State);
        Assert.Equal(1, signatureCollectedEvents);

        var signerBFirstInput = SignInputs(testPsbt.BasePsbt, testPsbt.SignerB, 0);
        pendingTransaction = await pendingTransactionService.CollectSignature(
            new PendingTransactionService.PendingTransactionFullId("BTC", storeId, pendingTransaction.Id),
            signerBFirstInput,
            CancellationToken.None);
        blob = pendingTransaction!.GetBlob();
        Assert.Equal(2, blob.CollectedSignatures.Count);
        Assert.Equal(1, blob.SignaturesCollected);
        Assert.Equal(PendingTransactionState.Pending, pendingTransaction.State);
        Assert.Equal(2, signatureCollectedEvents);

        var signerBSecondInput = SignInputs(testPsbt.BasePsbt, testPsbt.SignerB, 1);
        pendingTransaction = await pendingTransactionService.CollectSignature(
            new PendingTransactionService.PendingTransactionFullId("BTC", storeId, pendingTransaction.Id),
            signerBSecondInput,
            CancellationToken.None);
        blob = pendingTransaction!.GetBlob();
        Assert.Equal(3, blob.CollectedSignatures.Count);
        Assert.Equal(2, blob.SignaturesCollected);
        Assert.Equal(0, Math.Max(0, (blob.SignaturesNeeded ?? 0) - (blob.SignaturesCollected ?? 0)));
        Assert.Equal(PendingTransactionState.Signed, pendingTransaction.State);
        Assert.Equal(3, signatureCollectedEvents);

        var secondPendingTransaction = await pendingTransactionService.CreatePendingTransaction(
            storeId,
            "BTC",
            testPsbt.BasePsbt,
            requestBaseUrl,
            cancellationToken: CancellationToken.None);
        secondPendingTransaction = await pendingTransactionService.CollectSignature(
            new PendingTransactionService.PendingTransactionFullId("BTC", storeId, secondPendingTransaction.Id),
            signerAAllInputs,
            CancellationToken.None);
        blob = secondPendingTransaction!.GetBlob();
        Assert.Equal(1, blob.SignaturesCollected);

        var signerBAllInputs = SignInputs(testPsbt.BasePsbt, testPsbt.SignerB, 0, 1);
        secondPendingTransaction = await pendingTransactionService.CollectSignature(
            new PendingTransactionService.PendingTransactionFullId("BTC", storeId, secondPendingTransaction.Id),
            signerBAllInputs,
            CancellationToken.None);
        blob = secondPendingTransaction!.GetBlob();
        Assert.Equal(2, blob.SignaturesCollected);
        Assert.Equal(PendingTransactionState.Signed, secondPendingTransaction.State);
        Assert.Equal(2, blob.CollectedSignatures.Count);
    }

    [Fact]
    [Trait("Integration", "Integration")]
    public async Task PendingMultisigTransactionCountsFinalizedInputsAndSelfHealsStoredProgress()
    {
        var dbTester = CreateDBTester();
        await dbTester.MigrateAsync();
        var dbContextFactory = dbTester.CreateContextFactory();
        var storeId = await CreateTestStore(dbContextFactory);
        var pendingTransactionService = new PendingTransactionService(
            CreateNetworkProvider(),
            dbTester.CreateContextFactory(),
            new EventAggregator(BTCPayLogs),
            LoggerFactory.CreateLogger<PendingTransactionService>());
        var requestBaseUrl = RequestBaseUrl.FromUrl("https://example.com");
        var testPsbt = CreatePendingMultisigPsbt();

        var pendingTransaction = await pendingTransactionService.CreatePendingTransaction(
            storeId,
            "BTC",
            testPsbt.BasePsbt,
            requestBaseUrl,
            cancellationToken: CancellationToken.None);

        var finalizedInputSubmission = SignInputs(testPsbt.BasePsbt, testPsbt.SignerA, 0, 1);
        finalizedInputSubmission.Inputs[0].Sign(testPsbt.SignerB);
        finalizedInputSubmission.Inputs[0].FinalizeInput();
        pendingTransaction = await pendingTransactionService.CollectSignature(
            new PendingTransactionService.PendingTransactionFullId("BTC", storeId, pendingTransaction.Id),
            finalizedInputSubmission,
            CancellationToken.None);

        var blob = pendingTransaction!.GetBlob();
        Assert.Equal(1, blob.SignaturesCollected);
        Assert.Equal(PendingTransactionState.Pending, pendingTransaction.State);
        Assert.True(finalizedInputSubmission.Inputs[0].IsFinalized());
        Assert.NotNull(finalizedInputSubmission.Inputs[0].FinalScriptWitness);
        Assert.Empty(finalizedInputSubmission.Inputs[0].PartialSigs);

        await using (var ctx = dbContextFactory.CreateContext())
        {
            var staleTransaction = await ctx.PendingTransactions.FindAsync(pendingTransaction.Id);
            Assert.NotNull(staleTransaction);
            staleTransaction!.SetBlob(new PendingTransactionBlob
            {
                PSBT = blob.PSBT,
                RequestBaseUrl = blob.RequestBaseUrl,
                CollectedSignatures = blob.CollectedSignatures,
                SignaturesNeeded = 0,
                SignaturesTotal = 0,
                SignaturesCollected = 99
            });
            await ctx.SaveChangesAsync();
        }

        var refreshed = await pendingTransactionService.GetPendingTransaction(
            new PendingTransactionService.PendingTransactionFullId("BTC", storeId, pendingTransaction.Id));
        blob = refreshed!.GetBlob();
        Assert.Equal(2, blob.SignaturesNeeded);
        Assert.Equal(3, blob.SignaturesTotal);
        Assert.Equal(1, blob.SignaturesCollected);

        await using (var ctx = dbContextFactory.CreateContext())
        {
            var staleTransaction = await ctx.PendingTransactions.FindAsync(pendingTransaction.Id);
            Assert.NotNull(staleTransaction);
            staleTransaction!.SetBlob(new PendingTransactionBlob
            {
                PSBT = blob.PSBT,
                RequestBaseUrl = blob.RequestBaseUrl,
                CollectedSignatures = blob.CollectedSignatures,
                SignaturesNeeded = 9,
                SignaturesTotal = 9,
                SignaturesCollected = 9
            });
            await ctx.SaveChangesAsync();
        }

        var refreshedList = await pendingTransactionService.GetPendingTransactions("BTC", storeId);
        blob = Assert.Single(refreshedList).GetBlob();
        Assert.Equal(2, blob.SignaturesNeeded);
        Assert.Equal(3, blob.SignaturesTotal);
        Assert.Equal(1, blob.SignaturesCollected);
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
        await s.Page.ClickAsync("#CreatePendingTransaction");

        // validating the state of UI
        Assert.Equal("0", await s.Page.TextContentAsync("#Sigs_0__Collected"));
        Assert.Equal("2", await s.Page.TextContentAsync("#Sigs_0__Missing"));
        Assert.Equal("2/3", await s.Page.TextContentAsync("#Sigs_0__Scheme"));

        // now proceeding to click on sign button and sign transactions
        await SignPendingTransactionWithKey(s, address, derivationScheme, resp1);
        Assert.Equal("1", await s.Page.TextContentAsync("#Sigs_0__Collected"));
        Assert.Equal("1", await s.Page.TextContentAsync("#Sigs_0__Missing"));

        await SignPendingTransactionWithKey(s, address, derivationScheme, resp2);
        Assert.Equal("2", await s.Page.TextContentAsync("#Sigs_0__Collected"));
        Assert.Equal("0", await s.Page.TextContentAsync("#Sigs_0__Missing"));

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

        // Collecting without a user id should still update the pending transaction.
        await s.GoToWallet(navPages: WalletsNavPages.Send);
        await s.Page.FillAsync("#Outputs_0__DestinationAddress", address);
        await s.Page.FillAsync("#Outputs_0__Amount", "0.3");
        await s.Page.ClickAsync("#CreatePendingTransaction");

        var pendingTransactionService = s.Server.PayTester.GetService<PendingTransactionService>();
        var pendingWithoutSigner = Assert.Single(await pendingTransactionService.GetPendingTransactions(cryptoCode, storeId));
        var pendingWithoutSignerBlob = pendingWithoutSigner.GetBlob();
        Assert.NotNull(pendingWithoutSignerBlob?.PSBT);
        var signedWithoutSigner = SignWithSeed(pendingWithoutSignerBlob.PSBT, derivationScheme, resp1);
        await pendingTransactionService.CollectSignature(
            new PendingTransactionService.PendingTransactionFullId(cryptoCode, storeId, pendingWithoutSigner.Id),
            PSBT.Parse(signedWithoutSigner, network.NBitcoinNetwork),
            CancellationToken.None,
            signerUserId: null);

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
        await s.Logout();
        await s.GoToRegister();
        var employee = await s.RegisterNewUser();
        await s.SkipWizard();

        await using var scope = s.Server.PayTester.GetService<IServiceScopeFactory>().CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var storeRepo = scope.ServiceProvider.GetRequiredService<StoreRepository>();
        var multisigService = scope.ServiceProvider.GetRequiredService<MultisigService>();

        var signerAUser = await userManager.FindByEmailAsync(signerA);
        var signerBUser = await userManager.FindByEmailAsync(signerB);
        var walletManagerUser = await userManager.FindByEmailAsync(walletManager);
        var employeeUser = await userManager.FindByEmailAsync(employee);
        Assert.NotNull(signerAUser);
        Assert.NotNull(signerBUser);
        Assert.NotNull(walletManagerUser);
        Assert.NotNull(employeeUser);

        var walletSignerRole = new StoreRoleId(storeId, "Wallet Signer");
        await storeRepo.AddOrUpdateStoreRole(walletSignerRole, new[] { WalletPolicies.CanSignWalletTransactions });
        await storeRepo.AddOrUpdateStoreUser(storeId, signerAUser.Id, walletSignerRole);
        await storeRepo.AddOrUpdateStoreUser(storeId, signerBUser.Id, walletSignerRole);
        await storeRepo.AddOrUpdateStoreUser(storeId, walletManagerUser.Id, new StoreRoleId("Wallet Manager"));
        await storeRepo.AddOrUpdateStoreUser(storeId, employeeUser.Id, StoreRoleId.Employee);

        async Task<string> CreateRequest(params string[] participantEmails)
        {
            await s.GoToHome();
            await s.Logout();
            await s.GoToLogin();
            await s.LogIn(owner);
            await s.GoToUrl($"/stores/{storeId}/onchain/BTC/import/multisig");
            await Expect(s.Page.Locator($"label.multisig-signer-item:has-text('{employee}')")).ToHaveCountAsync(0);
            foreach (var participantEmail in participantEmails)
            {
                await s.Page.Locator($"label.multisig-signer-item:has-text('{participantEmail}') input[type='checkbox']").CheckAsync();
            }
            await s.Page.FillAsync("#MultisigRequiredSigners", "2");
            await s.Page.FillAsync("#MultisigTotalSigners", "2");
            await s.Page.SelectOptionAsync("#MultisigScriptType", "p2wsh");
            var signerRequestEmail = await s.Server.AssertHasEmail(async () =>
            {
                await s.Page.ClickAsync("#CreateSignerRequest");
            });
            Assert.Equal("Multisig signer request for BTC", signerRequestEmail.Subject);
            var currentPending = (await multisigService.GetPendingMultisigSetup(storeId))[0];
            Assert.NotNull(currentPending);
            Assert.Equal(storeId, currentPending.StoreId);
            Assert.Equal("BTC", currentPending.CryptoCode);
            Assert.Equal(2, currentPending.Participants.Count);
            Assert.All(currentPending.Participants, p => Assert.True(string.IsNullOrEmpty(p.AccountKey)));
            Assert.Contains($"/multisig-setups/{currentPending.RequestId}", signerRequestEmail.Html ?? signerRequestEmail.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith($"/multisig-setups/{currentPending.RequestId}", new Uri(s.Page.Url).AbsolutePath);
            await Expect(s.Page.Locator("#MultisigRequiredSigners")).ToHaveCountAsync(0);
            await Expect(s.Page.Locator("#MultisigTotalSigners")).ToHaveCountAsync(0);
            await Expect(s.Page.Locator("#MultisigScriptType")).ToHaveCountAsync(0);
            await Expect(s.Page.Locator("#CreateSignerRequest")).ToHaveCountAsync(0);
            return currentPending.RequestId;
        }

        async Task SubmitSigner(string email, string requestId, string accountKey, RootedKeyPath accountKeyPath, string expectedMessage, bool submitForm = true, bool useVaultCallback = false, bool expectNotification = true)
        {
            await s.GoToHome();
            await s.Logout();
            await s.GoToLogin();
            await s.LogIn(email);
            if (useVaultCallback)
            {
                await InstallMockVaultAsync(s.Page, accountKey, accountKeyPath.MasterFingerprint.ToString());
            }
            await s.GoToUrl(submitForm
                ? $"/multisig-setups/{Uri.EscapeDataString(requestId)}"
                : $"/multisig-setups/{Uri.EscapeDataString(requestId)}/signer-key");
            if (submitForm)
            {
                await Expect(s.Page.Locator("#SubmitSignerKeyCta")).ToBeVisibleAsync();
                await s.Page.ClickAsync("#SubmitSignerKeyCta");
                if (useVaultCallback)
                {
                    await s.Page.ClickAsync("#SubmitSignerKeyHardware");
                    await Expect(s.Page.Locator("#SubmitSignerKeyButton")).ToBeDisabledAsync();
                    await Expect(s.Page.Locator("#SignerKeyFields")).ToHaveClassAsync(new Regex("d-none"));
                    await Expect(s.Page.Locator("#vault-confirm")).ToBeVisibleAsync();
                    await Expect(s.Page.Locator("#keyPath")).ToHaveValueAsync("m/48'/1'/0'/2'");
                    await s.Page.FillAsync("#accountNumber", "3");
                    await Expect(s.Page.Locator("#keyPath")).ToHaveValueAsync("m/48'/1'/3'/2'");
                    await s.Page.FillAsync("#accountNumber", "0");
                    await Expect(s.Page.Locator("#keyPath")).ToHaveValueAsync("m/48'/1'/0'/2'");
                    await s.Page.ClickAsync("#vault-confirm");
                    await TestUtils.EventuallyAsync(async () =>
                    {
                        var vaultRequests = await s.Page.EvaluateAsync<string[]>("window.__vaultRequests || []");
                        Assert.Contains(vaultRequests, command => command.Contains("getxpub", StringComparison.OrdinalIgnoreCase));
                    }, 30000);
                    await Expect(s.Page.Locator("#DisplayAccountKey")).ToHaveValueAsync(accountKey, new LocatorAssertionsToHaveValueOptions { Timeout = 30000 });
                    await Expect(s.Page.Locator("#SignerKeyFields")).Not.ToHaveClassAsync(new Regex("d-none"));
                    await Expect(s.Page.Locator("#SubmitSignerKeyButton")).ToBeEnabledAsync();
                    Assert.Equal(accountKey, await s.Page.InputValueAsync("#DisplayAccountKey"));
                    Assert.Equal(accountKeyPath.ToString(), await s.Page.InputValueAsync("#AccountKeyPath"));
                }
                else
                {
                    await s.Page.ClickAsync("#SubmitSignerKeyManual");
                    await s.Page.FillAsync("#DisplayAccountKey", accountKey);
                    await s.Page.FillAsync("#AccountKeyPath", accountKeyPath.ToString());
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
        await s.GoToUrl($"/stores/{storeId}/onchain/BTC/import/multisig");
        await s.Page.EvaluateAsync(
            "(employeeId) => { const button = document.querySelector('#CreateSignerRequest'); const form = button.form; const input = document.createElement('input'); input.type = 'hidden'; input.name = 'MultisigParticipantUserIds'; input.value = employeeId; form.appendChild(input); button.disabled = false; }",
            employeeUser.Id);
        await s.Page.ClickAsync("#CreateSignerRequest");
        await Expect(s.Page.Locator("body")).ToContainTextAsync("One or more selected users are invalid.");
        var pendingAfterInvalidSigner = (await multisigService.GetPendingMultisigSetup(storeId))[0];
        Assert.DoesNotContain(pendingAfterInvalidSigner.Participants, p => p.UserId == employeeUser.Id);

        await s.GoToHome();
        var dashboardMultisigSetup = s.Page.Locator($"#SetupGuide-Multisig-{firstRequestId}");
        await Expect(dashboardMultisigSetup).ToBeVisibleAsync();
        await Expect(dashboardMultisigSetup).ToContainTextAsync("Multisig wallet setup");
        var firstSessionPath = $"/multisig-setups/{Uri.EscapeDataString(firstRequestId)}";

        await s.Page.Locator("#menu-item-Account").WaitForAsync();
        await s.Logout();
        await s.GoToLogin();
        await s.LogIn(employee);
        await s.GoToUrl(firstSessionPath, ignoreResponse: true);
        Assert.Contains("/errors/403", new Uri(s.Page.Url).AbsolutePath, StringComparison.OrdinalIgnoreCase);

        await SubmitSigner(signerA, firstRequestId, signerAKey.AccountKey, signerAKey.AccountKeyPath, "Signer key submitted successfully.", useVaultCallback: true);
        var pendingAfterFirstSubmit =  (await multisigService.GetPendingMultisigSetup(storeId))[0];
        var signerAParticipant = pendingAfterFirstSubmit.Participants.Single(p => p.UserId == signerAUser.Id);
        Assert.Equal(signerAKey.AccountKey, signerAParticipant.AccountKey);

        await SubmitSigner(signerA, firstRequestId, signerBKey.AccountKey, signerBKey.AccountKeyPath, "Your signer key is submitted.", submitForm: false);
        var pendingAfterResubmit =  (await multisigService.GetPendingMultisigSetup(storeId))[0];
        var signerAAfterResubmit = pendingAfterResubmit.Participants.Single(p => p.UserId == signerAUser.Id);
        Assert.Equal(signerAKey.AccountKey, signerAAfterResubmit.AccountKey);

        await SubmitSigner(signerB, firstRequestId, signerAKey.AccountKey, signerAKey.AccountKeyPath, "This signer key is already used in this multisig request.", expectNotification: false);
        var pendingAfterDuplicate =  (await multisigService.GetPendingMultisigSetup(storeId))[0];
        var signerBParticipant = pendingAfterDuplicate.Participants.Single(p => p.UserId == signerBUser.Id);
        Assert.True(string.IsNullOrWhiteSpace(signerBParticipant.AccountKey));

        await s.GoToHome();
        await s.Logout();
        await s.GoToLogin();
        await s.LogIn(owner);
        await s.GoToUrl(firstSessionPath);
        await Expect(s.Page.Locator("#FinalizeMultisig")).ToHaveCountAsync(0);

        var secondRequestId = await CreateRequest(walletManager, signerB);
        Assert.NotEqual(firstRequestId, secondRequestId);
        var pendingAfterSecondRequest = (await multisigService.GetPendingMultisigSetup(storeId))[0];
        Assert.Equal(secondRequestId, pendingAfterSecondRequest?.RequestId);
        var staleSessionResponse = await s.Page.GotoAsync(s.Link(firstSessionPath), new() { WaitUntil = WaitUntilState.Commit });
        Assert.Equal(404, staleSessionResponse?.Status);
        await SubmitSigner(walletManager, secondRequestId, walletManagerKey.AccountKey, walletManagerKey.AccountKeyPath, "Signer key submitted successfully.");
        await SubmitSigner(signerB, secondRequestId, signerBKey.AccountKey, signerBKey.AccountKeyPath, "Signer key submitted successfully.");

        await s.GoToHome();
        await s.Logout();
        await s.GoToLogin();
        await s.LogIn(signerA);
        await s.GoToUrl($"/multisig-setups/{Uri.EscapeDataString(secondRequestId)}", ignoreResponse: true);
        Assert.Contains("/errors/403", new Uri(s.Page.Url).AbsolutePath, StringComparison.OrdinalIgnoreCase);

        await s.GoToHome();
        await s.Logout();
        await s.GoToLogin();
        await s.LogIn(walletManager);
        await s.GoToUrl($"/multisig-setups/{Uri.EscapeDataString(secondRequestId)}");
        await Expect(s.Page.Locator("#Nav-Wallets a").Filter(new() { HasText = "Send" })).ToHaveCountAsync(0);
        await Expect(s.Page.Locator("#Nav-Wallets a").Filter(new() { HasText = "Receive" })).ToHaveCountAsync(0);
        await Expect(s.Page.Locator("body")).ToContainTextAsync("2/2 submitted");
        await Expect(s.Page.Locator("#FinalizeMultisig")).ToBeVisibleAsync();
        await s.Page.ClickAsync("#FinalizeMultisig");
        await Expect(s.Page.Locator("#Confirm")).ToBeVisibleAsync();
        await Expect(s.Page.Locator("#CancelWizard")).ToHaveAttributeAsync("href", $"/stores/{storeId}/index");
        var walletCreatedEmail = await s.Server.AssertHasEmail(async () =>
        {
            await s.Page.ClickAsync("#Confirm");
        });
        Assert.Equal("Multisig wallet created for BTC", walletCreatedEmail.Subject);
        await Expect(s.Page.Locator("body")).ToContainTextAsync(walletManager);
        await Expect(s.Page.Locator("body")).ToContainTextAsync(signerB);
    }

    private async Task<MailPitClient.Message> SignPendingTransactionWithKey(PlaywrightTester s, string address,
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
        {
            await s.Page.ClickAsync("#Collect");
            return null;
        }
        else
        {
            var signatureCollectedEmail = await s.Server.AssertHasEmail(async () =>
            {
                await s.Page.ClickAsync("#Collect");
            });
            Assert.Equal(expectedNotificationSubject, signatureCollectedEmail.Subject);
            return signatureCollectedEmail;
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

    private static (string AccountKey, RootedKeyPath AccountKeyPath) CreateTestMultisigSignerKey(string mnemonic, string accountKeyPath = "84'/1'/0'")
    {
        var rootKey = new Mnemonic(mnemonic).DeriveExtKey();
        var path = KeyPath.Parse(accountKeyPath);
        return (
            rootKey.Derive(path).Neuter().ToString(Network.RegTest),
            new RootedKeyPath(rootKey.GetPublicKey().GetHDFingerPrint(),
            KeyPath.Parse($"m/{path}")));
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

    private static TestPendingMultisigPsbt CreatePendingMultisigPsbt()
    {
        var network = Network.RegTest;
        var signerA = new Key();
        var signerB = new Key();
        var signerC = new Key();
        var witnessScript = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(2, signerA.PubKey, signerB.PubKey, signerC.PubKey);
        var scriptPubKey = witnessScript.WitHash.ScriptPubKey;

        var previousTransactionA = network.CreateTransaction();
        previousTransactionA.Outputs.Add(Money.Coins(1.0m), scriptPubKey);
        var previousTransactionB = network.CreateTransaction();
        previousTransactionB.Outputs.Add(Money.Coins(1.1m), scriptPubKey);

        var builder = network.CreateTransactionBuilder();
        builder.AddCoins(
            previousTransactionA.Outputs.AsCoins().First().ToScriptCoin(witnessScript),
            previousTransactionB.Outputs.AsCoins().First().ToScriptCoin(witnessScript));
        builder.Send(new Key().PubKey.WitHash.ScriptPubKey, Money.Coins(1.5m));
        builder.SetChange(new Key().PubKey.WitHash.ScriptPubKey);
        builder.SendFees(Money.Satoshis(10_000));

        return new TestPendingMultisigPsbt(builder.BuildPSBT(false), signerA, signerB, signerC);
    }

    private static PSBT SignInputs(PSBT basePsbt, Key signer, params int[] inputIndexes)
    {
        var psbt = basePsbt.Clone();
        foreach (var inputIndex in inputIndexes)
        {
            psbt.Inputs[inputIndex].Sign(signer);
        }

        return psbt;
    }

    private static async Task<string> CreateTestStore(ApplicationDbContextFactory contextFactory)
    {
        await using var ctx = contextFactory.CreateContext();
        var store = new StoreData
        {
            Id = Guid.NewGuid().ToString("N"),
            StoreName = "Test Store",
            DerivationStrategies = "{}",
            StoreBlob = "{}"
        };
        ctx.Stores.Add(store);
        await ctx.SaveChangesAsync();
        return store.Id;
    }

    private StoreRepository CreateStoreRepository(ApplicationDbContextFactory contextFactory)
    {
        var eventAggregator = new EventAggregator(BTCPayLogs);
        var settingsRepository = new SettingsRepository(contextFactory, eventAggregator, new MemoryCache(new MemoryCacheOptions()));
        return new StoreRepository(contextFactory, new JsonSerializerSettings(), eventAggregator, settingsRepository);
    }

    private sealed record TestPendingMultisigPsbt(PSBT BasePsbt, Key SignerA, Key SignerB, Key SignerC);
}
