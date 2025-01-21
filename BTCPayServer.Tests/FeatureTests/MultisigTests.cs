using System;
using System.Collections.Generic;
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
    public async Task CanEnableMultisigWallet()
    {
        using var s = CreateSeleniumTester();
        await s.StartAsync();
        // var invoiceRepository = s.Server.PayTester.GetService<InvoiceRepository>();
        s.RegisterNewUser(true);
        
        var cryptoCode = "BTC";
        s.CreateNewStore();

        var explorerProvider = s.Server.PayTester.GetService<ExplorerClientProvider>();
        var client = explorerProvider.GetExplorerClient(cryptoCode);
        var req = new GenerateWalletRequest { ScriptPubKeyType = ScriptPubKeyType.Segwit, SavePrivateKeys = true };

        var resp1 = await client.GenerateWalletAsync(req);
        s.TestLogs.LogInformation($"Created hot wallet 1: {resp1.DerivationScheme} | {resp1.AccountKeyPath} | {resp1.MasterHDKey}");
        var resp2 = await client.GenerateWalletAsync(req);
        s.TestLogs.LogInformation($"Created hot wallet 2: {resp2.DerivationScheme} | {resp2.AccountKeyPath} | {resp2.MasterHDKey}");
        var resp3 = await client.GenerateWalletAsync(req);
        s.TestLogs.LogInformation($"Created hot wallet 3: {resp3.DerivationScheme} | {resp3.AccountKeyPath} | {resp3.MasterHDKey}");

        var multisigDerivationScheme = $"wsh(multi(2,[{resp1.AccountKeyPath}]{resp1.DerivationScheme}/0/*," +
                                       $"[{resp2.AccountKeyPath}]{resp2.DerivationScheme}/0/*," +
                                       $"[{resp3.AccountKeyPath}]{resp3.DerivationScheme}/0/*))";
        
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
        var signedPsbt = await SignWithSeed(psbt, resp1);
        
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


    public async Task<string> SignWithSeed(string psbtBase64, GenerateWalletResponse resp)
    {
        // Parse master HD key and root path
        var strMasterHdKey = resp.MasterHDKey;
        var extKey = new BitcoinExtKey(strMasterHdKey, Network.RegTest);

        var strKeypath = resp.AccountKeyPath.ToStringWithEmptyKeyPathAware();
        RootedKeyPath rootedKeyPath = RootedKeyPath.Parse(strKeypath);

        // Create the derivation scheme
        var derivationScheme = new DirectDerivationStrategy(extKey.Neuter(), true);

        // Parse the PSBT
        var psbt = PSBT.Parse(psbtBase64, Network.RegTest);

        // Ensure the key paths in the PSBT align with the signing key
        if (rootedKeyPath.MasterFingerprint != extKey.GetPublicKey().GetHDFingerPrint())
        {
            throw new Exception("Master fingerprint mismatch. Ensure the wallet matches the PSBT.");
        }

        // Rebase the PSBT key paths
        psbt.RebaseKeyPaths(extKey.Neuter(), rootedKeyPath);

        // Derive the signing key for the specific account path
        var signingKey = extKey;

        // Sign the PSBT
        psbt.Settings.SigningOptions = new SigningOptions();
        var changed = psbt.PSBTChanged(() => psbt.SignAll(derivationScheme, signingKey, rootedKeyPath));

        if (!changed)
        {
            throw new Exception("Failed to sign the PSBT. Ensure the inputs align with the account key path.");
        }

        // Return the updated and signed PSBT
        return psbt.ToBase64();
    }
}
