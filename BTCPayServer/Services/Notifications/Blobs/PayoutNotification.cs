using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Models.NotificationViewModels;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Services.Notifications.Blobs
{
    public class PayoutNotification
    {
        internal class Handler : NotificationHandler<PayoutNotification>
        {
            private readonly LinkGenerator _linkGenerator;
            private readonly BTCPayServerOptions _options;

            public Handler(LinkGenerator linkGenerator, BTCPayServerOptions options)
            {
                _linkGenerator = linkGenerator;
                _options = options;
            }
            public override string NotificationType => "payout";
            protected override void FillViewModel(PayoutNotification notification, NotificationViewModel vm)
            {
                vm.Body = "A new payout is awaiting for approval";
                vm.ActionLink = _linkGenerator.GetPathByAction(nameof(WalletsController.Payouts),
                    "Wallets",
                    new { walletId = new WalletId(notification.StoreId, notification.PaymentMethod) }, _options.RootPath);
            }
        }
        public string PayoutId { get; set; }
        public string StoreId { get; set; }
        public string PaymentMethod { get; set; }
        public string Currency { get; set; }
    }
}
