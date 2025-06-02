#nullable enable
using System.Globalization;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Services.PaymentRequests;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;

namespace BTCPayServer.HostedServices.Webhooks;

public class PaymentRequestWebhookDeliveryRequest(
    PaymentRequestEvent evt,
    string webhookId,
    WebhookEvent webhookEvent,
    WebhookDeliveryData delivery,
    WebhookBlob webhookBlob)
    : WebhookSender.WebhookDeliveryRequest(webhookId, webhookEvent, delivery, webhookBlob)
{
    public override Task<SendEmailRequest?> Interpolate(SendEmailRequest req,
        UIStoresController.StoreEmailRule storeEmailRule)
    {
        var blob = evt.Data.GetBlob();
        if (storeEmailRule.CustomerEmail &&
            MailboxAddressValidator.TryParse(blob.Email, out var bmb))
        {
            req.Email ??= string.Empty;
            req.Email += $",{bmb}";
        }

        req.Subject = Interpolate(req.Subject, evt.Data);
        req.Body = Interpolate(req.Body, evt.Data);
        return Task.FromResult(req)!;
    }

    private string Interpolate(string str, PaymentRequestData data)
    {
        var id = data.Id;
        var trimmedId = $"{id.Substring(0, 7)}...{id.Substring(id.Length - 7)}";

        var blob = data.GetBlob();
        var res = str.Replace("{PaymentRequest.Id}", id)
            .Replace("{PaymentRequest.TrimmedId}", trimmedId)
            .Replace("{PaymentRequest.Amount}", data.Amount.ToString(CultureInfo.InvariantCulture))
            .Replace("{PaymentRequest.Currency}", data.Currency)
            .Replace("{PaymentRequest.Title}", blob.Title)
            .Replace("{PaymentRequest.Description}", blob.Description)
            .Replace("{PaymentRequest.ReferenceId}", data.ReferenceId)
            .Replace("{PaymentRequest.Status}", evt.Data.Status.ToString());

        res = InterpolateJsonField(res, "PaymentRequest.FormResponse", blob.FormResponse);
        return res;
    }
}
