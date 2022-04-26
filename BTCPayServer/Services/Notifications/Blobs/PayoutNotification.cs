using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Models.NotificationViewModels;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Services.Notifications.Blobs
{
    public class PayoutNotification : BaseNotification
    {
        private const string TYPE = "payout";

        internal class Handler : NotificationHandler<PayoutNotification>
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
                    return new (string identifier, string name)[] { (TYPE, "Payouts") };
                }
            }

            protected override void FillViewModel(PayoutNotification notification, NotificationViewModel vm)
            {
                vm.Body = (notification.Status ?? PayoutState.AwaitingApproval) switch
                {
                    PayoutState.AwaitingApproval => $"A new payout is awaiting for approval",
                    PayoutState.AwaitingPayment => $"A new payout is approved and awaiting payment",
                    _ => throw new ArgumentOutOfRangeException()
                };
                vm.ActionLink = _linkGenerator.GetPathByAction(nameof(UIStorePullPaymentsController.Payouts),
                    "UIStorePullPayments",
                    new { storeId = notification.StoreId, paymentMethodId = notification.PaymentMethod }, _options.RootPath);
            }
        }

        public string PayoutId { get; set; }
        public string StoreId { get; set; }
        public string PaymentMethod { get; set; }
        public string Currency { get; set; }
        public PayoutState? State { get; set; }
        public override string Identifier => TYPE;
        public override string NotificationType => TYPE;
        public PayoutState? Status { get; set; }
    }
}
