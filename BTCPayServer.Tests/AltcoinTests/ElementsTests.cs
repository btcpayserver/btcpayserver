using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using NBitcoin;
using NBitcoin.Payment;
using NBitpayClient;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class ElementsTests
    {

        public const int TestTimeout = 60_000;
        public ElementsTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact]
        [Trait("Altcoins", "Altcoins")]
        public async Task OnlyShowSupportedWallets()
        {
            using (var tester = ServerTester.Create())
            {
                tester.ActivateLBTC();
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("LBTC");
                user.RegisterDerivationScheme("BTC");
                user.RegisterDerivationScheme("USDT");

                Assert.Equal(3, Assert.IsType<ListWalletsViewModel>(Assert.IsType<ViewResult>(await user.GetController<WalletsController>().ListWallets()).Model).Wallets.Count);
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void LoadSubChainsAlways()
        {
            var options = new BTCPayServerOptions();
            options.LoadArgs(new ConfigurationRoot(new List<IConfigurationProvider>()
            {
                new MemoryConfigurationProvider(new MemoryConfigurationSource()
                {
                    InitialData = new[]
                    {
                        new KeyValuePair<string, string>("chains", "usdt"),
                    }
                })
            }));

            Assert.NotNull(options.NetworkProvider.GetNetwork("LBTC"));
            Assert.NotNull(options.NetworkProvider.GetNetwork("USDT"));
        }


        [Fact]
        [Trait("Altcoins", "Altcoins")]
        public async Task ElementsAssetsAreHandledCorrectly()
        {
            using (var tester = ServerTester.Create())
            {
                tester.ActivateLBTC();
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("LBTC");
                user.RegisterDerivationScheme("USDT");
                user.RegisterDerivationScheme("ETB");
                await tester.LBTCExplorerNode.GenerateAsync(4);
                //no tether on our regtest, lets create it and set it
                var tether = tester.NetworkProvider.GetNetwork<ElementsBTCPayNetwork>("USDT");
                var lbtc = tester.NetworkProvider.GetNetwork<ElementsBTCPayNetwork>("LBTC");
                var etb = tester.NetworkProvider.GetNetwork<ElementsBTCPayNetwork>("ETB");
                var issueAssetResult = await tester.LBTCExplorerNode.SendCommandAsync("issueasset", 100000, 0);
                tether.AssetId = uint256.Parse(issueAssetResult.Result["asset"].ToString());
                ((ElementsBTCPayNetwork)tester.PayTester.GetService<BTCPayWalletProvider>().GetWallet("USDT").Network)
                    .AssetId = tether.AssetId;
                Assert.Equal(tether.AssetId, tester.NetworkProvider.GetNetwork<ElementsBTCPayNetwork>("USDT").AssetId);
                Assert.Equal(tether.AssetId, ((ElementsBTCPayNetwork)tester.PayTester.GetService<BTCPayWalletProvider>().GetWallet("USDT").Network).AssetId);

                var issueAssetResult2 = await tester.LBTCExplorerNode.SendCommandAsync("issueasset", 100000, 0);
                etb.AssetId = uint256.Parse(issueAssetResult2.Result["asset"].ToString());
                ((ElementsBTCPayNetwork)tester.PayTester.GetService<BTCPayWalletProvider>().GetWallet("ETB").Network)
                    .AssetId = etb.AssetId;


                //test: register 2 assets on the same elements network and make sure paying an invoice on one does not affect the other in any way
                var invoice = await user.BitPay.CreateInvoiceAsync(new Invoice(0.1m, "BTC"));
                Assert.Equal(3, invoice.SupportedTransactionCurrencies.Count);
                var ci = invoice.CryptoInfo.Single(info => info.CryptoCode.Equals("LBTC"));
                //1 lbtc = 1 btc
                Assert.Equal(1, ci.Rate);
                var star = await tester.LBTCExplorerNode.SendCommandAsync("sendtoaddress", ci.Address, ci.Due, "", "", false, true,
                    1, "UNSET", lbtc.AssetId);

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("paid", localInvoice.Status);
                    Assert.Single(localInvoice.CryptoInfo.Single(info => info.CryptoCode.Equals("LBTC")).Payments);
                });

                invoice = await user.BitPay.CreateInvoiceAsync(new Invoice(0.1m, "BTC"));

                ci = invoice.CryptoInfo.Single(info => info.CryptoCode.Equals("USDT"));
                Assert.Equal(3, invoice.SupportedTransactionCurrencies.Count);
                star = await tester.LBTCExplorerNode.SendCommandAsync("sendtoaddress", ci.Address, ci.Due, "", "", false, true,
                    1, "UNSET", tether.AssetId);

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("paid", localInvoice.Status);
                    Assert.Single(localInvoice.CryptoInfo.Single(info => info.CryptoCode.Equals("USDT", StringComparison.InvariantCultureIgnoreCase)).Payments);
                });

                //test precision based on https://github.com/ElementsProject/elements/issues/805#issuecomment-601277606
                var etbBip21 = new BitcoinUrlBuilder(invoice.CryptoInfo.Single(info => info.CryptoCode == "ETB").PaymentUrls.BIP21.Replace(etb.UriScheme, "bitcoin"), etb.NBitcoinNetwork);
                //precision = 2, 1ETB  = 0.00000100
                Assert.Equal(100, etbBip21.Amount.Satoshi);

                var lbtcBip21 = new BitcoinUrlBuilder(invoice.CryptoInfo.Single(info => info.CryptoCode == "LBTC").PaymentUrls.BIP21.Replace(lbtc.UriScheme, "bitcoin"), lbtc.NBitcoinNetwork);
                //precision = 8, 0.1 = 0.1
                Assert.Equal(0.1m, lbtcBip21.Amount.ToDecimal(MoneyUnit.BTC));
            }
        }
    }
}
