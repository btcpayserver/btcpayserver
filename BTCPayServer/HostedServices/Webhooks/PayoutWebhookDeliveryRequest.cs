#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Services;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;

namespace BTCPayServer.HostedServices.Webhooks;

public class PayoutWebhookDeliveryRequest : WebhookSender.WebhookDeliveryRequest
{
    private readonly PayoutEvent _evt;
    private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;

    public PayoutWebhookDeliveryRequest(PayoutEvent evt, string webhookId, WebhookEvent webhookEvent,
        WebhookDeliveryData delivery, WebhookBlob webhookBlob, BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings) : base(webhookId, webhookEvent, delivery, webhookBlob)
    {
        _evt = evt;
        _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
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
        var res=  str.Replace("{Payout.Id}", _evt.Payout.Id)
            .Replace("{Payout.PullPaymentId}", _evt.Payout.PullPaymentDataId)
            .Replace("{Payout.Destination}", _evt.Payout.Destination)
            .Replace("{Payout.State}", _evt.Payout.State.ToString());

        var blob = _evt.Payout.GetBlob(_btcPayNetworkJsonSerializerSettings);

        res = InterpolateJsonField(str, "Payout.Metadata", blob.Metadata);
        return res;
    }
}
