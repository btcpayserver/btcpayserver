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
            applicationBuilder.AddSingleton<IUIExtension>(new UIExtension("NFC/CheckoutEnd",
                "checkout-end"));
            applicationBuilder.AddSingleton<IUIExtension>(new UIExtension("NFC/LNURLNFCPostContent",
                "checkout-lightning-post-content"));
            applicationBuilder.AddSingleton<IUIExtension>(new UIExtension("NFC/CheckoutEnd",
                "checkout-v2-end"));
            applicationBuilder.AddSingleton<IUIExtension>(new UIExtension("NFC/LNURLNFCPostContent-v2",
                "checkout-v2-lightning-post-content"));
            applicationBuilder.AddSingleton<IUIExtension>(new UIExtension("NFC/LNURLNFCPostContent-v2",
                "checkout-v2-bitcoin-post-content"));
            base.Execute(applicationBuilder);
        }
    }
}
