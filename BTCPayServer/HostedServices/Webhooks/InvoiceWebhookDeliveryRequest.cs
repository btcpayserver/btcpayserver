using System.Globalization;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;

namespace BTCPayServer.HostedServices.Webhooks;

public class InvoiceWebhookDeliveryRequest(
    InvoiceEntity invoice,
    string webhookId,
    WebhookEvent webhookEvent,
    WebhookDeliveryData delivery,
    WebhookBlob webhookBlob)
    : WebhookSender.WebhookDeliveryRequest(webhookId, webhookEvent, delivery, webhookBlob)
{
    public InvoiceEntity Invoice { get; } = invoice;

    public override Task<SendEmailRequest> Interpolate(SendEmailRequest req,
        UIStoresController.StoreEmailRule storeEmailRule)
    {
        if (storeEmailRule.CustomerEmail &&
            MailboxAddressValidator.TryParse(Invoice.Metadata.BuyerEmail, out var bmb))
        {
            req.Email ??= string.Empty;
            req.Email += $",{bmb}";
        }

        req.Subject = Interpolate(req.Subject);
        req.Body = Interpolate(req.Body);
        return Task.FromResult(req);
    }

    private string Interpolate(string str)
    {
        var res = str.Replace("{Invoice.Id}", Invoice.Id)
            .Replace("{Invoice.StoreId}", Invoice.StoreId)
            .Replace("{Invoice.Price}", Invoice.Price.ToString(CultureInfo.InvariantCulture))
            .Replace("{Invoice.Currency}", Invoice.Currency)
            .Replace("{Invoice.Status}", Invoice.Status.ToString())
            .Replace("{Invoice.AdditionalStatus}", Invoice.ExceptionStatus.ToString())
            .Replace("{Invoice.OrderId}", Invoice.Metadata.OrderId);


        res = InterpolateJsonField(res, "Invoice.Metadata", Invoice.Metadata.ToJObject());
        return res;
    }

    public override string ToString()
        => $"Webhook delivery request ({WebhookEvent.Type})";
}
