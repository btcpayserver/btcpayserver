using BTCPayServer.Configuration;
using BTCPayServer.Contracts;
using BTCPayServer.Controllers;
using BTCPayServer.Models.NotificationViewModels;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Services.Notifications.Blobs
{
    public class ExternalPayoutTransactionNotification : BaseNotification
    {
        private const string TYPE = "external-payout-transaction";

        internal class Handler : NotificationHandler<ExternalPayoutTransactionNotification>
        {
            private readonly LinkGenerator _linkGenerator;
            private readonly BTCPayServerOptions _options;

            public Handler(LinkGenerator linkGenerator, BTCPayServerOptions options)
            {
                _linkGenerator = linkGenerator;
                _options = options;
            }

            public override string NotificationType => TYPE;

            public override (string identifier, string name)[] Meta
            {
                get
                {
                    return new (string identifier, string name)[] {(TYPE, "External payout approval")};
                }
            }

            protected override void FillViewModel(ExternalPayoutTransactionNotification notification,
                NotificationViewModel vm)
            {
                vm.Body =
                    "A payment that was made to an approved payout by an external wallet is waiting for your confirmation.";
                vm.ActionLink = _linkGenerator.GetPathByAction(nameof(WalletsController.Payouts),
                    "Wallets",
                    new {walletId = new WalletId(notification.StoreId, notification.PaymentMethod)}, _options.RootPath);
            }
        }

        public string PayoutId { get; set; }
        public string StoreId { get; set; }
        public string PaymentMethod { get; set; }
        public override string Identifier => TYPE;
        public override string NotificationType => TYPE;
    }
}
