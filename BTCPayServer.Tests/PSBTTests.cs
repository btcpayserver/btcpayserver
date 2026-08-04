using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Tests
{
    public class PSBTTests(ITestOutputHelper helper) : UnitTestBase(helper)
    {
        [Fact]
        public void AddsWitnessUtxoToSegwitInputs()
        {
            var network = Network.RegTest;
            var keys = new[] { new Key(), new Key() };
            var witnessScript = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(
                2,
                keys.Select(key => key.PubKey).ToArray());
            var nativeP2wsh = witnessScript.WitHash.ScriptPubKey;
            var nativeP2wpkh = new Key().PubKey.WitHash.ScriptPubKey;
            var nestedP2wshRedeem = witnessScript.WitHash.ScriptPubKey;
            var nestedP2wsh = nestedP2wshRedeem.Hash.ScriptPubKey;
            var taproot = new Key().PubKey.GetScriptPubKey(ScriptPubKeyType.TaprootBIP86);
            var futureWitness = PayToWitTemplate.Instance.GenerateScriptPubKey(OpcodeType.OP_2, new byte[32]);
            var legacyP2sh = witnessScript.Hash.ScriptPubKey;

            var previousTransaction = network.CreateTransaction();
            previousTransaction.Outputs.Add(Money.Coins(1.0m), nativeP2wsh);
            previousTransaction.Outputs.Add(Money.Coins(1.1m), nativeP2wpkh);
            previousTransaction.Outputs.Add(Money.Coins(1.2m), nestedP2wsh);
            previousTransaction.Outputs.Add(Money.Coins(1.3m), taproot);
            previousTransaction.Outputs.Add(Money.Coins(1.4m), futureWitness);
            previousTransaction.Outputs.Add(Money.Coins(1.5m), legacyP2sh);

            var spendingTransaction = network.CreateTransaction();
            for (var i = 0; i < previousTransaction.Outputs.Count; i++)
                spendingTransaction.Inputs.Add(new OutPoint(previousTransaction.GetHash(), i));
            spendingTransaction.Outputs.Add(Money.Coins(4.5m), new Key().PubKey.WitHash.ScriptPubKey);

            var psbt = PSBT.FromTransaction(spendingTransaction, network);
            foreach (var input in psbt.Inputs)
                input.NonWitnessUtxo = previousTransaction;
            psbt.Inputs[0].WitnessScript = witnessScript;
            psbt.Inputs[2].RedeemScript = nestedP2wshRedeem;
            psbt.Inputs[2].WitnessScript = witnessScript;
            psbt.Inputs[5].RedeemScript = witnessScript;

            Assert.All(psbt.Inputs, input => Assert.Null(input.WitnessUtxo));

            psbt.AddWitnessUtxoToSegwitInputs();

            for (var i = 0; i < 5; i++)
            {
                Assert.NotNull(psbt.Inputs[i].WitnessUtxo);
                Assert.Equal(previousTransaction.Outputs[i], psbt.Inputs[i].WitnessUtxo);
            }
            Assert.Null(psbt.Inputs[5].WitnessUtxo);
            Assert.All(psbt.Inputs, input => Assert.Same(previousTransaction, input.NonWitnessUtxo));
        }

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
