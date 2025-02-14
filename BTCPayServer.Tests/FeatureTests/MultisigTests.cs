using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.BIP78.Sender;
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
    public const int TestTimeout = 60_000;

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
        
        var strategy = ParseDerivationStrategy(multisigDerivationScheme, network);
        strategy.Source = "ManualDerivationScheme";
        var derivationScheme = strategy.AccountDerivation;

        var testPSBT =
            "cHNidP8BAH0CAAAAAWNgMvezbLK99DapvvVGAcfLpsb8MciqkyxACbjkIci8AQAAAAD9////AtTjXAAAAAAAIgAgztW7uUKcgZpup41zH9TE/SIR86N5nlpA/AF2vcFab1qAlpgAAAAAABYAFDoOrYFP5zUu7/P1SjpFKupWEzhwcQAAAAABASuMxPUAAAAAACIAILouk+nHd2D+GBCUcH/I3br1i+VDi189C6s1ehiE0EJPAQDqAgAAAAABARjKYDsBT9T08w5i8LvUzjllbxGu7op86rRU+51+mf//AAAAAAD9////AoAhECkBAAAAFgAUUjB02g5VDuAgU2jiuqtZR0RRlzSMxPUAAAAAACIAILouk+nHd2D+GBCUcH/I3br1i+VDi189C6s1ehiE0EJPAkcwRAIgSCMjFcTAus9/MMQVmofwJsPUOJT5SnexvVLFJtE+eCUCIB+WtzrRiNWjTs45npxQYU3zj/Bbwgjn/wWyBRs2Efs2ASECWnI1s9ozQRL2qbK6JbLHzj9LlU9Pras3nZfq/njBJwg0AAAAAQVpUiEC7aReaC4gC3y/hovEu4JEpTIeUJreLFaR+liCBCQkfzshAhCnQ9PzA+Bsiz/2dSAt07YMSXITpp0nhYeKhcs5vp09IQKFIqThm1/7rfFM6EeXuIyGoc5s9uqf9eqZMwfBs5VSN1OuIgYCEKdD0/MD4GyLP/Z1IC3TtgxJchOmnSeFh4qFyzm+nT0Y7n02xFQAAIABAACAAAAAgAAAAAADAAAAIgYChSKk4Ztf+63xTOhHl7iMhqHObPbqn/XqmTMHwbOVUjcYbAFPs1QAAIABAACAAAAAgAAAAAADAAAAIgYC7aReaC4gC3y/hovEu4JEpTIeUJreLFaR+liCBCQkfzsYV7P0OlQAAIABAACAAAAAgAAAAAADAAAAAAEBaVIhA73+AdFyNMI/L8pW8k8L7V2xtk7ffjIgU8PlaLoDQuc5IQJP1ot2JmtrPDUxCk9jf40t9rHwnvSIRUcARX+fhEM4NSECXESl6tjPWdrMeBHsfMf/8XX1SfhaLJX6fRX9fHynx45TriICAk/Wi3Yma2s8NTEKT2N/jS32sfCe9IhFRwBFf5+EQzg1GO59NsRUAACAAQAAgAAAAIABAAAAAAAAACICAlxEperYz1nazHgR7HzH//F19Un4WiyV+n0V/Xx8p8eOGGwBT7NUAACAAQAAgAAAAIABAAAAAAAAACICA73+AdFyNMI/L8pW8k8L7V2xtk7ffjIgU8PlaLoDQuc5GFez9DpUAACAAQAAgAAAAIABAAAAAAAAAAAA";
        
        var signedPsbt = await SignWithSeed(testPSBT, derivationScheme, resp2);
        s.TestLogs.LogInformation($"Signed PSBT: {signedPsbt}");
    }

    [Fact]
    [Trait("Selenium", "Selenium")]
    public async Task CanEnableMultisigWallet()
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
        
        var strategy = ParseDerivationStrategy(multisigDerivationScheme, network);
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
        
        // TODO: Add sending of transaction
        // fetch address from receive page
        s.Driver.FindElement(By.Id("WalletNav-Receive")).Click();
        var address = s.Driver.FindElement(By.Id("Address")).GetAttribute("data-text");
        s.Driver.FindElement(By.XPath("//button[@value='fill-wallet']")).Click();
        s.Driver.FindElement(By.Id("CancelWizard")).Click();
        
        s.Driver.FindElement(By.Id("WalletNav-Send")).Click();
        s.Driver.FindElement(By.Id("Outputs_0__DestinationAddress")).SendKeys(address);
        var amount = "0.1";
        s.Driver.FindElement(By.Id("Outputs_0__Amount")).SendKeys(amount);
        s.Driver.FindElement(By.Id("CreatePendingTransaction")).Click();
        
        s.Driver.WaitForElement(By.XPath("//a[text()='View']")).Click();

        var transactionRow = s.Driver.FindElement(By.XPath($"//tr[td[text()='{address}']]"));
         Assert.NotNull(transactionRow);

        var signTransactionButton = s.Driver.FindElement(By.Id("SignTransaction"));
        Assert.NotNull(signTransactionButton);
        
        s.Driver.FindElement(By.Id("PSBTOptionsExportHeader")).Click();
        s.Driver.FindElement(By.Id("ShowRawVersion")).Click();
        
        
        //
        var psbt = s.Driver.FindElement(By.Id("psbt-base64")).Text;
        while (String.IsNullOrEmpty(psbt))
        {
            psbt = s.Driver.FindElement(By.Id("psbt-base64")).Text;
        }
        var signedPsbt = await SignWithSeed(psbt, derivationScheme, resp1);
        
        s.Driver.FindElement(By.Id("PSBTOptionsImportHeader")).Click();
        s.Driver.FindElement(By.Id("ImportedPSBT")).SendKeys(signedPsbt);
        
        s.Driver.FindElement(By.Id("Decode")).Click();

        // TODO: In future add signing of PSBT transaction here and check if it is signed
        
        var cancelTransactionButton = s.Driver.FindElement(By.XPath("//a[text()='Cancel']"));
        Assert.NotNull(cancelTransactionButton);
        cancelTransactionButton.Click();

        s.Driver.FindElement(By.Id("ConfirmContinue")).Click();

        Assert.Contains("Aborted Pending Transaction", s.FindAlertMessage().Text);
        
        s.TestLogs.LogInformation($"Finished MultiSig Flow");
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


    public async Task<string> SignWithSeed(string psbtBase64, DerivationStrategyBase derivationStrategyBase,
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
    
    
    
    private DerivationSchemeSettings ParseDerivationStrategy(string derivationScheme, BTCPayNetwork network)
    {
        var parser = new DerivationSchemeParser(network);
        var isOD = Regex.Match(derivationScheme, @"\(.*?\)");
        if (isOD.Success)
        {
            var derivationSchemeSettings = new DerivationSchemeSettings();
            var result = parser.ParseOutputDescriptor(derivationScheme);
            derivationSchemeSettings.AccountOriginal = derivationScheme.Trim();
            derivationSchemeSettings.AccountDerivation = result.Item1;
            derivationSchemeSettings.AccountKeySettings = result.Item2?.Select((path, i) => new AccountKeySettings()
            {
                RootFingerprint = path?.MasterFingerprint,
                AccountKeyPath = path?.KeyPath,
                AccountKey = result.Item1.GetExtPubKeys().ElementAt(i).GetWif(parser.Network)
            }).ToArray() ?? new AccountKeySettings[result.Item1.GetExtPubKeys().Count()];
            return derivationSchemeSettings;
        }

        var strategy = parser.Parse(derivationScheme);
        return new DerivationSchemeSettings(strategy, network);
    }
}
