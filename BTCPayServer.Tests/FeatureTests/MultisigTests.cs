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
            "70736274ff0100890200000001e63e1ff5adeb1aaa5c8a9a26357e4a310d05919230ba1892d3b8c58287c81a100000000000fdffffff026bbfbe0000000000220020ced5bbb9429c819a6ea78d731fd4c4fd2211f3a3799e5a40fc0176bdc15a6f5a8096980000000000220020bf8c6925c264858c49e3f7774cff5e302415739f5e833f538ea23b292ae0776b000000000001012bb56557010000000022002042359c43b5d9e6d816552bbd38d5e87e4d8b8b3d2a0297e83674729db35245ae010569522103fb277070c09f77253ccdab1fa9fc0a91fbdbf0f954dc6a0e8a6ae4eed49e510b2102f47fb03e782521089f1f0890286d1708a91fe25d44e45f3ac8940eabbcaca0dc2103366012ebd257fc2577b0cc4c4f447fc415c584b8316a7d7a58f70527be9593dc53ae220602f47fb03e782521089f1f0890286d1708a91fe25d44e45f3ac8940eabbcaca0dc18ee7d36c4540000800100008000000080000000000f000000220603366012ebd257fc2577b0cc4c4f447fc415c584b8316a7d7a58f70527be9593dc186c014fb3540000800100008000000080000000000f000000220603fb277070c09f77253ccdab1fa9fc0a91fbdbf0f954dc6a0e8a6ae4eed49e510b1857b3f43a540000800100008000000080000000000f00000000010169522103bdfe01d17234c23f2fca56f24f0bed5db1b64edf7e322053c3e568ba0342e73921024fd68b76266b6b3c35310a4f637f8d2df6b1f09ef488454700457f9f8443383521025c44a5ead8cf59dacc7811ec7cc7fff175f549f85a2c95fa7d15fd7c7ca7c78e53ae2202024fd68b76266b6b3c35310a4f637f8d2df6b1f09ef488454700457f9f8443383518ee7d36c454000080010000800000008001000000000000002202025c44a5ead8cf59dacc7811ec7cc7fff175f549f85a2c95fa7d15fd7c7ca7c78e186c014fb35400008001000080000000800100000000000000220203bdfe01d17234c23f2fca56f24f0bed5db1b64edf7e322053c3e568ba0342e7391857b3f43a54000080010000800000008001000000000000000000";
        
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
