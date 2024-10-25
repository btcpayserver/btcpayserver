using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Events;
using ExchangeSharp;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Services.Notifications.Blobs
{
    internal class InvoiceEventNotification : BaseNotification
    {
        private const string TYPE = "invoicestate";
        internal class Handler : NotificationHandler<InvoiceEventNotification>
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
                    return new (string identifier, string name)[] { (TYPE, StringLocalizer["All invoice updates"]), }
                        .Concat(TextMapping.Select(pair => ($"{TYPE}_{pair.Key}", StringLocalizer["Invoice {0}", pair.Value].Value))).ToArray();
                }
            }

            private Dictionary<string, string> TextMapping => new()
            {
                // {InvoiceEvent.PaidInFull, StringLocalizer["was fully paid"},
                {InvoiceEvent.PaidAfterExpiration, StringLocalizer["was paid after expiration"]},
                {InvoiceEvent.ExpiredPaidPartial, StringLocalizer["expired with partial payments"]},
                {InvoiceEvent.FailedToConfirm, StringLocalizer["has payments that failed to confirm on time"]},
                // {InvoiceEvent.ReceivedPayment, StringLocalizer["received a payment"},
                {InvoiceEvent.Confirmed, StringLocalizer["is settled"]}
            };

            protected override void FillViewModel(InvoiceEventNotification notification,
                NotificationViewModel vm)
            {
                var baseStr = StringLocalizer["Invoice {0}..", notification.InvoiceId.Substring(0, 5)];
                if (TextMapping.ContainsKey(notification.Event))
                {
                    vm.Body = $"{baseStr} {TextMapping[notification.Event]}";
                }
                vm.Identifier = notification.Identifier;
                vm.Type = notification.NotificationType;
                vm.StoreId = notification.StoreId;
                vm.ActionLink = _linkGenerator.GetPathByAction(nameof(UIInvoiceController.Invoice),
                    "UIInvoice",
                    new { invoiceId = notification.InvoiceId }, _options.RootPath);
            }
        }

        public InvoiceEventNotification()
        {
        }

        public InvoiceEventNotification(string invoiceId, string invoiceEvent, string storeId)
        {
            InvoiceId = invoiceId;
            Event = invoiceEvent;
            StoreId = storeId;
        }

        public static bool HandlesEvent(string invoiceEvent)
        {
            return ((string[])[
                InvoiceEvent.PaidAfterExpiration,
                InvoiceEvent.ExpiredPaidPartial,
                InvoiceEvent.FailedToConfirm,
                InvoiceEvent.Confirmed])
                .Any(s => s == invoiceEvent);
        }

        public string InvoiceId { get; set; }
        public string Event { get; set; }
        public string StoreId { get; set; }
        public override string Identifier => Event is null ? TYPE : Event.ToStringLowerInvariant();
        public override string NotificationType => TYPE;
    }
}
