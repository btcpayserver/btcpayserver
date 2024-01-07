#nullable enable
using System.Globalization;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Services.PaymentRequests;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;

namespace BTCPayServer.HostedServices.Webhooks;

public class PaymentRequestWebhookDeliveryRequest : WebhookSender.WebhookDeliveryRequest
{
    private readonly PaymentRequestEvent _evt;

    public PaymentRequestWebhookDeliveryRequest(PaymentRequestEvent evt, string webhookId, WebhookEvent webhookEvent,
        WebhookDeliveryData delivery, WebhookBlob webhookBlob) : base(webhookId, webhookEvent, delivery, webhookBlob)
    {
        _evt = evt;
    }

    public override Task<SendEmailRequest?> Interpolate(SendEmailRequest req,
        UIStoresController.StoreEmailRule storeEmailRule)
    {
        var blob = _evt.Data.GetBlob();
        if (storeEmailRule.CustomerEmail &&
            MailboxAddressValidator.TryParse(blob.Email, out var bmb))
        {
            req.Email ??= string.Empty;
            req.Email += $",{bmb}";
        }

        req.Subject = Interpolate(req.Subject, blob);
        req.Body = Interpolate(req.Body, blob);
        return Task.FromResult(req)!;
    }

    private string Interpolate(string str, PaymentRequestBaseData blob)
    {
        var res = str.Replace("{PaymentRequest.Id}", _evt.Data.Id)
            .Replace("{PaymentRequest.Price}", blob.Amount.ToString(CultureInfo.InvariantCulture))
            .Replace("{PaymentRequest.Currency}", blob.Currency)
            .Replace("{PaymentRequest.Title}", blob.Title)
            .Replace("{PaymentRequest.Description}", blob.Description)
            .Replace("{PaymentRequest.Status}", _evt.Data.Status.ToString());

        res = InterpolateJsonField(res, "PaymentRequest.FormResponse", blob.FormResponse);
        return res;
    }
}
