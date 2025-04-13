using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using ExchangeSharp;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Services.Notifications.Blobs
{
    public class PayoutNotification : BaseNotification
    {
        private const string TYPE = "payout";

        internal class Handler : NotificationHandler<PayoutNotification>
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
                    return [(TYPE, StringLocalizer["Payouts"])];
                }
            }

            protected override void FillViewModel(PayoutNotification notification, NotificationViewModel vm)
            {
                vm.Identifier = notification.Identifier;
                vm.Type = notification.NotificationType;
                vm.StoreId = notification.StoreId;
                vm.Body = (notification.Status ?? PayoutState.AwaitingApproval) switch
                {
                    PayoutState.AwaitingApproval => StringLocalizer["A new payout is awaiting for approval"],
                    PayoutState.AwaitingPayment => StringLocalizer["A new payout is approved and awaiting payment"],
                    _ => throw new ArgumentOutOfRangeException()
                };
                vm.ActionLink = _linkGenerator.GetPathByAction(nameof(UIStorePullPaymentsController.Payouts),
                    "UIStorePullPayments",
                    new { storeId = notification.StoreId, payoutMethodId = notification.PaymentMethod }, _options.RootPath);
            }
        }

        public string PayoutId { get; set; }
        public string StoreId { get; set; }
        public string PaymentMethod { get; set; }
        public string Currency { get; set; }
        public override string Identifier => Status is null ? TYPE : $"{TYPE}_{Status.ToStringLowerInvariant()}";

        public override string NotificationType => TYPE;
        public PayoutState? Status { get; set; }
    }
}
