using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Events;
using BTCPayServer.Models.NotificationViewModels;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Services.Notifications.Blobs
{
    internal class InvoiceEventNotification:BaseNotification
    {
        private const string TYPE = "invoicestate";
        internal class Handler : NotificationHandler<InvoiceEventNotification>
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
                    return new (string identifier, string name)[] {(TYPE, "All invoice updates"),}
                        .Concat(TextMapping.Select(pair => ($"{TYPE}_{pair.Key}", $"Invoice {pair.Value}"))).ToArray();
                }
            }

            internal static Dictionary<string, string> TextMapping = new Dictionary<string, string>()
            {
                // {InvoiceEvent.PaidInFull, "was fully paid."},
                {InvoiceEvent.PaidAfterExpiration, "was paid after expiration."},
                {InvoiceEvent.ExpiredPaidPartial, "expired with partial payments."},
                {InvoiceEvent.FailedToConfirm, "has payments that failed to confirm on time."},
                // {InvoiceEvent.ReceivedPayment, "received a payment."},
                {InvoiceEvent.Confirmed, "was confirmed paid."}
            };

            protected override void FillViewModel(InvoiceEventNotification notification,
                NotificationViewModel vm)
            {
                var baseStr = $"Invoice {notification.InvoiceId.Substring(0, 5)}..";
                if (TextMapping.ContainsKey(notification.Event))
                {
                    vm.Body = $"{baseStr} {TextMapping[notification.Event]}";
                }
                vm.ActionLink = _linkGenerator.GetPathByAction(nameof(InvoiceController.Invoice),
                    "Invoice",
                    new { invoiceId = notification.InvoiceId }, _options.RootPath);
            }
        }

        public InvoiceEventNotification()
        {
        }

        public InvoiceEventNotification(string invoiceId, string invoiceEvent)
        {
            InvoiceId = invoiceId;
            Event = invoiceEvent;
        }

        public static bool HandlesEvent(string invoiceEvent)
        {
            return Handler.TextMapping.Keys.Any(s => s == invoiceEvent);
        }

        public string InvoiceId { get; set; }
        public string Event { get; set; }
        public override string Identifier => $"{TYPE}_{Event}";
        public override string NotificationType => TYPE;
    }
}
