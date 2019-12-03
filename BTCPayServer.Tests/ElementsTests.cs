using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Models.WalletViewModels;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using Xunit;

namespace BTCPayServer.Tests
{
    public class ElementsTests
    {
        [Fact]
        public async Task OnlyShowSupportedWallets()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                await tester.EnsureChannelsSetup();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("LBTC");
                user.RegisterDerivationScheme("BTC");
                user.RegisterDerivationScheme("USDT");
                
                Assert.Single(Assert.IsType<ListWalletsViewModel>(Assert.IsType<ViewResult>(user.GetController<WalletsController>().ListWallets()).Model).Wallets);
            }
        }

        [Fact]
        public async Task ElementsAssetsAreHandledCorrectly()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("LBTC");
                user.RegisterDerivationScheme("USDT");
                
                //no tether on our regtest, lets create it and set it
                var tether = tester.NetworkProvider.GetNetwork<ElementsBTCPayNetwork>("USDT");
                var issueAssetResult = await tester.LBTCExplorerNode.SendCommandAsync("issueasset", 1000, 0);
                tether.AssetId = uint256.Parse(issueAssetResult.Result["asset"].ToString());
                
                Assert.Equal(tether.AssetId,  tester.NetworkProvider.GetNetwork<ElementsBTCPayNetwork>("USDT").AssetId);
                //test: register 2 assets on the same elements network and make sure paying an invoice on one does not affect the other in any way
                var invoice = await user.BitPay.CreateInvoiceAsync(new Invoice(100, "BTC"));
                Assert.Equal(2, invoice.SupportedTransactionCurrencies.Count);



            }
        }
    }
}
