using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Services.Notifications.Blobs
{
    public class ExternalPayoutTransactionNotification : BaseNotification
    {
        private const string TYPE = "external-payout-transaction";

        internal class Handler : NotificationHandler<ExternalPayoutTransactionNotification>
        {
            private readonly LinkGenerator _linkGenerator;
            private readonly BTCPayServerOptions _options;
            private IStringLocalizer StringLocalizer { get; }

            public Handler(LinkGenerator linkGenerator, BTCPayServerOptions options, IStringLocalizer stringLocalizer)
            {
                _linkGenerator = linkGenerator;
                _options = options;
                StringLocalizer = stringLocalizer;
            }

            public override string NotificationType => TYPE;

            public override (string identifier, string name)[] Meta
            {
                get
                {
                    return new (string identifier, string name)[] { (TYPE, StringLocalizer["External payout approval"]) };
                }
            }

            protected override void FillViewModel(ExternalPayoutTransactionNotification notification,
                NotificationViewModel vm)
            {
                vm.Identifier = notification.Identifier;
                vm.Type = notification.NotificationType;
                vm.StoreId = notification.StoreId;
                vm.Body =
                    StringLocalizer["A payment that was made to an approved payout by an external wallet is waiting for your confirmation."];
                vm.ActionLink = _linkGenerator.GetPathByAction(nameof(UIStorePullPaymentsController.Payouts),
                    "UIStorePullPayments",
                    new
                    {
                        storeId = notification.StoreId,
                        payoutMethodId = notification.PaymentMethod,
                        payoutState = PayoutState.AwaitingPayment
                    }, _options.RootPath);
            }
        }

        public string PayoutId { get; set; }
        public string StoreId { get; set; }
        public string PaymentMethod { get; set; }
        public override string Identifier => TYPE;
        public override string NotificationType => TYPE;
    }
}
