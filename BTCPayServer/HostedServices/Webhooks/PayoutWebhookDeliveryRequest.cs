#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Services;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;

namespace BTCPayServer.HostedServices.Webhooks;

public class PayoutWebhookDeliveryRequest(PayoutEvent evt, string? webhookId, WebhookEvent webhookEvent,
        WebhookDeliveryData? delivery, WebhookBlob? webhookBlob,
        BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings)
    : WebhookSender.WebhookDeliveryRequest(webhookId!, webhookEvent, delivery!, webhookBlob!)
{
    public override Task<SendEmailRequest?> Interpolate(SendEmailRequest req,
        UIStoresController.StoreEmailRule storeEmailRule)
    {
        req.Subject = Interpolate(req.Subject);
        req.Body = Interpolate(req.Body);
        return Task.FromResult(req)!;
    }

    private string Interpolate(string str)
    {
        var blob = evt.Payout.GetBlob(btcPayNetworkJsonSerializerSettings);
        var res = str.Replace("{Payout.Id}", evt.Payout.Id)
            .Replace("{Payout.PullPaymentId}", evt.Payout.PullPaymentDataId)
            .Replace("{Payout.Destination}", evt.Payout.DedupId ?? blob.Destination)
            .Replace("{Payout.State}", evt.Payout.State.ToString());

        res = InterpolateJsonField(res, "Payout.Metadata", blob.Metadata);
        return res;
    }
}
