#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;

namespace BTCPayServer.HostedServices.Webhooks;

public class PayoutWebhookDeliveryRequest : WebhookSender.WebhookDeliveryRequest
{
    private readonly PayoutEvent _evt;

    public PayoutWebhookDeliveryRequest(PayoutEvent evt, string webhookId, WebhookEvent webhookEvent,
        WebhookDeliveryData delivery, WebhookBlob webhookBlob) : base(webhookId, webhookEvent, delivery, webhookBlob)
    {
        _evt = evt;
    }

    public override Task<SendEmailRequest?> Interpolate(SendEmailRequest req,
        UIStoresController.StoreEmailRule storeEmailRule)
    {
        
        req.Subject = Interpolate(req.Subject);
        req.Body = Interpolate(req.Body);
        return Task.FromResult(req)!;
    }

    private string Interpolate(string str)
    {
        return str.Replace("{Payout.Id}", _evt.Payout.Id)
            .Replace("{Payout.PullPaymentId}", _evt.Payout.PullPaymentDataId)
            .Replace("{Payout.Destination}", _evt.Payout.Destination)
            .Replace("{Payout.State}", _evt.Payout.State.ToString());
    }
}
