using System.Collections;
using System.Globalization;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Wallets;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;

namespace BTCPayServer.HostedServices.Webhooks;

public class PendingTransactionDeliveryRequest(
    PendingTransactionService.PendingTransactionEvent evt,
    string webhookId,
    WebhookEvent webhookEvent,
    WebhookDeliveryData delivery,
    WebhookBlob webhookBlob)
    : WebhookSender.WebhookDeliveryRequest(webhookId, webhookEvent, delivery, webhookBlob)
{
    public override Task<SendEmailRequest> Interpolate(SendEmailRequest req,
        UIStoresController.StoreEmailRule storeEmailRule)
    {
        var blob = evt.Data.GetBlob();
        // if (storeEmailRule.CustomerEmail &&
        //     MailboxAddressValidator.TryParse(Invoice.Metadata.BuyerEmail, out var bmb))
        // {
        //     req.Email ??= string.Empty;
        //     req.Email += $",{bmb}";
        // }

        req.Subject = Interpolate(req.Subject, blob);
        req.Body = Interpolate(req.Body, blob);
        return Task.FromResult(req);
    }

    private string Interpolate(string str, PendingTransactionBlob blob)
    {
        var id = evt.Data.TransactionId;
        string trimmedId = $"{id.Substring(0, 7)}...{id.Substring(id.Length - 7)}";
        
        var res = str.Replace("{PendingTransaction.Id}", id)
            .Replace("{PendingTransaction.TrimmedId}", trimmedId)
            .Replace("{PendingTransaction.StoreId}", evt.Data.StoreId)
            .Replace("{PendingTransaction.SignaturesCollected}", blob.SignaturesCollected?.ToString())
            .Replace("{PendingTransaction.SignaturesNeeded}", blob.SignaturesNeeded?.ToString())
            .Replace("{PendingTransaction.SignaturesTotal}", blob.SignaturesTotal?.ToString());
            
        // res = InterpolateJsonField(res, "Invoice.Metadata", Invoice.Metadata.ToJObject());
        return res;
    }
    
}
