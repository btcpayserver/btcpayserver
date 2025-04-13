using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Controllers;
using BTCPayServer.Events;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Views.Wallets;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests.FeatureTests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class MultisigTests : UnitTestBase
{
    public MultisigTests(ITestOutputHelper helper) : base(helper)
    {
    }

    [Fact]
    [Trait("Selenium", "Selenium")]
    public async Task SignTestPSBT()
    {
        var cryptoCode = "BTC";
        using var s = CreateSeleniumTester();
        await s.StartAsync();

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
        
        var strategy = UIStoresController.ParseDerivationStrategy(multisigDerivationScheme, network);
        strategy.Source = "ManualDerivationScheme";
        var derivationScheme = strategy.AccountDerivation;

        var testPSBT =
            "cHNidP8BAIkCAAAAAQmiSunnaKN7F4Jv5uHROfYbIZOckCck/Wo7gAQmi9hfAAAAAAD9////AtgbZgAAAAAAIgAgWCUFlU9eWkyxn0l0yQxs2rXQZ7d9Ry8LaYECaVC0TUGAlpgAAAAAACIAIFZxT+UIdhHZC4qFPhPQ6IXdX+44HIxCYcoh/bNOhB0hAAAAAAABAStAAf8AAAAAACIAIL2DDkfKwKHxZj2EKxXUd4uwf0IvPaCxUtAPq9snpq9TAQDqAgAAAAABAVuHuou9E5y6zUJaUreQD0wUeiPnT2aY+YU7QaPJOiQCAAAAAAD9////AkAB/wAAAAAAIgAgvYMOR8rAofFmPYQrFdR3i7B/Qi89oLFS0A+r2yemr1PM5AYpAQAAABYAFIlFupZkD07+GRo24WRS3IFcf+EuAkcwRAIgGi9wAcTfc0d0+j+Vg82aYklXCUsPg+g3jS+PTBTSQwkCIAPh5CZF18DTBKqWU2qdhNCbZ8Tp/NCEHjLJRHcH0oluASECWnI1s9ozQRL2qbK6JbLHzj9LlU9Pras3nZfq/njBJwhwAAAAAQVpUiECMCCasr2FRmRMiWkM/l1iraFR18td5SZ2APyQiaI0yY8hA8K96vH64BelUJiEPGwM6UTwRSfAJUR2j8dkw7i31fFTIQMlHLlaAPxw3fl1vaM1EofIirt79MXOryM54zpHwu1GlVOuIgIDwr3q8frgF6VQmIQ8bAzpRPBFJ8AlRHaPx2TDuLfV8VNHMEQCIANnprskJz8oVsetqOEViHtzhmSG8c36r3zmUIHwIoOhAiAZ1jBqj40iu2S/nMfiGyuCC/jSiSGik7YVwiwN+bbxPAEiBgIwIJqyvYVGZEyJaQz+XWKtoVHXy13lJnYA/JCJojTJjxhXs/Q6VAAAgAEAAIAAAACAAAAAAAUAAAAiBgMlHLlaAPxw3fl1vaM1EofIirt79MXOryM54zpHwu1GlRhsAU+zVAAAgAEAAIAAAACAAAAAAAUAAAAiBgPCverx+uAXpVCYhDxsDOlE8EUnwCVEdo/HZMO4t9XxUxjufTbEVAAAgAEAAIAAAACAAAAAAAUAAAAAAQFpUiEDa/J6SaiRjP1jhq9jpNxFKovEuWBz28seNMvsn0JC/ZIhA7p3bS7vLYB5UxlNN6YqkEDITyaMlk/i450q6+4woveAIQPTchIOrd+TNGBOX6il1HRZnBndyRoUj/hahbjTaAGHglOuIgIDa/J6SaiRjP1jhq9jpNxFKovEuWBz28seNMvsn0JC/ZIYV7P0OlQAAIABAACAAAAAgAEAAAABAAAAIgIDundtLu8tgHlTGU03piqQQMhPJoyWT+LjnSrr7jCi94AY7n02xFQAAIABAACAAAAAgAEAAAABAAAAIgID03ISDq3fkzRgTl+opdR0WZwZ3ckaFI/4WoW402gBh4IYbAFPs1QAAIABAACAAAAAgAEAAAABAAAAAAEBaVIhA/fCRR3MWwCgNuXMvlWLonY+TurUKOHXOSHALCck62deIQPqeQXD8ws9SDEDXSyD6a3WFlIGH+gDUf2/xAfw8HxE8iEC3LBRJYYxRzIeg9NxLGvtfATvFaKsO9D7AUjoTLZzke5TriICAtywUSWGMUcyHoPTcSxr7XwE7xWirDvQ+wFI6Ey2c5HuGGwBT7NUAACAAQAAgAAAAIAAAAAADAAAACICA+p5BcPzCz1IMQNdLIPprdYWUgYf6ANR/b/EB/DwfETyGO59NsRUAACAAQAAgAAAAIAAAAAADAAAACICA/fCRR3MWwCgNuXMvlWLonY+TurUKOHXOSHALCck62deGFez9DpUAACAAQAAgAAAAIAAAAAADAAAAAA=";
        
        var signedPsbt = SignWithSeed(testPSBT, derivationScheme, resp1);
        s.TestLogs.LogInformation($"Signed PSBT: {signedPsbt}");
    }

    [Fact]
    [Trait("Selenium", "Selenium")]
    public async Task CanEnableAndUseMultisigWallet()
    {
        var cryptoCode = "BTC";
        using var s = CreateSeleniumTester();
        await s.StartAsync();
        // var invoiceRepository = s.Server.PayTester.GetService<InvoiceRepository>();
        s.RegisterNewUser(true);
        
        var storeData = s.CreateNewStore();

        var explorerProvider = s.Server.PayTester.GetService<ExplorerClientProvider>();
        var client = explorerProvider.GetExplorerClient(cryptoCode);
        var req = new GenerateWalletRequest { ScriptPubKeyType = ScriptPubKeyType.Segwit, SavePrivateKeys = true };
        
        // var resp1 = await client.GenerateWalletAsync(req);
        // s.TestLogs.LogInformation($"Created hot wallet 1: {resp1.DerivationScheme} | {resp1.AccountKeyPath} | {resp1.MasterHDKey.ToWif()}");
        // var resp2 = await client.GenerateWalletAsync(req);
        // s.TestLogs.LogInformation($"Created hot wallet 2: {resp2.DerivationScheme} | {resp2.AccountKeyPath} | {resp2.MasterHDKey.ToWif()}");
        // var resp3 = await client.GenerateWalletAsync(req);
        // s.TestLogs.LogInformation($"Created hot wallet 3: {resp3.DerivationScheme} | {resp3.AccountKeyPath} | {resp3.MasterHDKey.ToWif()}");

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
        
        var strategy = UIStoresController.ParseDerivationStrategy(multisigDerivationScheme, network);
        strategy.Source = "ManualDerivationScheme";
        var derivationScheme = strategy.AccountDerivation;
        
        s.GoToWalletSettings();
        s.Driver.FindElement(By.Id("ImportWalletOptionsLink")).Click();
        s.Driver.FindElement(By.Id("ImportXpubLink")).Click();
        s.Driver.FindElement(By.Id("DerivationScheme")).SendKeys(multisigDerivationScheme);
        s.Driver.FindElement(By.Id("Continue")).Click();
        s.Driver.FindElement(By.Id("Confirm")).Click();
        s.TestLogs.LogInformation($"Multisig wallet setup: {multisigDerivationScheme}");
        
        // enabling multisig
        s.Driver.FindElement(By.Id("IsMultiSigOnServer")).Click();
        s.Driver.FindElement(By.Id("DefaultIncludeNonWitnessUtxo")).Click();
        s.Driver.FindElement(By.Id("SaveWalletSettings")).Click();
        Assert.Contains("Wallet settings successfully updated.", s.FindAlertMessage().Text);
        
        // fetch address from receive page
        s.Driver.FindElement(By.Id("WalletNav-Receive")).Click();
        var address = s.Driver.FindElement(By.Id("Address")).GetAttribute("data-text");
        s.Driver.FindElement(By.XPath("//button[@value='fill-wallet']")).Click();
        s.Driver.FindElement(By.Id("CancelWizard")).Click();
        
        // we are creating a pending transaction
        s.Driver.FindElement(By.Id("WalletNav-Send")).Click();
        s.Driver.FindElement(By.Id("Outputs_0__DestinationAddress")).SendKeys(address);
        var amount = "0.1";
        s.Driver.FindElement(By.Id("Outputs_0__Amount")).SendKeys(amount);
        s.Driver.FindElement(By.Id("CreatePendingTransaction")).Click();
        
        // validating the state of UI
        Assert.Equal("0", s.Driver.FindElement(By.Id("Sigs_0__Collected")).Text);
        Assert.Equal("2/3", s.Driver.FindElement(By.Id("Sigs_0__Scheme")).Text);
        
        // now proceeding to click on sign button and sign transactions
        SignPendingTransactionWithKey(s, address, derivationScheme, resp1);
        Assert.Equal("1", s.Driver.FindElement(By.Id("Sigs_0__Collected")).Text);
        
        SignPendingTransactionWithKey(s, address, derivationScheme, resp2);
        Assert.Equal("2", s.Driver.FindElement(By.Id("Sigs_0__Collected")).Text);

        // we should now have enough signatures to broadcast transaction
        s.Driver.WaitForElement(By.XPath("//a[text()='Broadcast']")).Click();
        s.Driver.FindElement(By.Id("BroadcastTransaction")).Click();
        Assert.Contains("Transaction broadcasted successfully", s.FindAlertMessage().Text);
        
        // now that we broadcast transaction, there shouldn't be broadcast button
        s.Driver.AssertElementNotFound(By.XPath("//a[text()='Broadcast']"));
        
        // Abort pending transaction flow
        s.Driver.FindElement(By.Id("WalletNav-Send")).Click();
        s.Driver.FindElement(By.Id("Outputs_0__DestinationAddress")).SendKeys(address);
        s.Driver.FindElement(By.Id("Outputs_0__Amount")).SendKeys("0.2");
        s.Driver.FindElement(By.Id("CreatePendingTransaction")).Click();
        
        s.Driver.FindElement(By.XPath("//a[text()='Abort']")).Click();

        s.Driver.FindElement(By.Id("ConfirmContinue")).Click();

        Assert.Contains("Aborted Pending Transaction", s.FindAlertMessage().Text);
        
        s.TestLogs.LogInformation($"Finished MultiSig Flow");
    }

    private void SignPendingTransactionWithKey(SeleniumTester s, string address,
        DerivationStrategyBase derivationScheme, GenerateWalletResponse signingKey)
    {
        // getting to pending transaction page
        s.Driver.WaitForElement(By.XPath("//a[text()='View']")).Click();

        var transactionRow = s.Driver.FindElement(By.XPath($"//tr[td[text()='{address}']]"));
        Assert.NotNull(transactionRow);

        var signTransactionButton = s.Driver.FindElement(By.Id("SignTransaction"));
        Assert.NotNull(signTransactionButton);

        // fetching PSBT
        s.Driver.FindElement(By.Id("PSBTOptionsExportHeader")).Click();
        s.Driver.WaitForElement(By.Id("ShowRawVersion")).Click();
        var psbt = s.Driver.WaitForElement(By.Id("psbt-base64")).Text;
        while (string.IsNullOrEmpty(psbt))
        {
            psbt = s.Driver.FindElement(By.Id("psbt-base64")).Text;
        }

        // signing PSBT and entering it to submit
        var signedPsbt = SignWithSeed(psbt, derivationScheme, signingKey);

        s.Driver.FindElement(By.Id("PSBTOptionsImportHeader")).Click();
        s.Driver.WaitForElement(By.Id("ImportedPSBT")).SendKeys(signedPsbt);

        s.Driver.FindElement(By.Id("Decode")).Click();
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
            DerivationScheme = parser.Parse(derivation),
            AccountKeyPath = RootedKeyPath.Parse(keypath)
        };
        return resp1;
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
