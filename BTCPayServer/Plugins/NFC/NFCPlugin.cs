using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.NFC
{
    public class NFCPlugin : BaseBTCPayServerPlugin
    {

        public override string Identifier => "BTCPayServer.Plugins.NFC";
        public override string Name => "NFC";
        public override string Description => "Allows you to support contactless card payments over NFC and LNURL Withdraw!";


        public override void Execute(IServiceCollection applicationBuilder)
        {
            applicationBuilder.AddUIExtension("checkout-end", "NFC/CheckoutEnd");
            applicationBuilder.AddUIExtension("checkout-lightning-post-content", "NFC/LNURLNFCPostContent");
            applicationBuilder.AddUIExtension("checkout-bitcoin-post-content", "NFC/LNURLNFCPostContent");
            base.Execute(applicationBuilder);
        }
    }
}
