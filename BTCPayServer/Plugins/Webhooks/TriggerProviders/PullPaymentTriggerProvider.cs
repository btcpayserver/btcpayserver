#nullable enable
using System.Globalization;
using System.Threading.Tasks;
using BTCPayServer.Abstractions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.Emails.HostedServices;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Webhooks.TriggerProviders;

public class PullPaymentTriggerProvider(
    LinkGenerator linkGenerator,
    ApplicationDbContextFactory dbContextFactory,
    ISettingsAccessor<ServerSettings> serverSettings)
    : WebhookTriggerProvider<PullPaymentEvent>
{
    protected override async Task<JObject> GetEmailModel(WebhookTriggerContext<PullPaymentEvent> webhookTriggerContext)
    {
        var evt = webhookTriggerContext.Event;
        var blob = evt.PullPayment.GetBlob();
        var model = await base.GetEmailModel(webhookTriggerContext);
        var id = evt.PullPayment.Id;
        var trimmedId = !string.IsNullOrEmpty(id) && id.Length > 15 ? $"{id.Substring(0, 7)}...{id.Substring(id.Length - 7)}" : id;
        var o = new JObject()
        {
            ["Id"] = id,
            ["TrimmedId"] = trimmedId,
            ["Name"] = blob.Name,
            ["Description"] = blob.Description,
            ["Amount"] = evt.PullPayment.Limit.ToString(CultureInfo.InvariantCulture),
            ["Currency"] = evt.PullPayment.Currency,
            ["AutoApproveClaims"] = blob.AutoApproveClaims,
            ["Archived"] = evt.PullPayment.Archived
        };
        model["PullPayment"] = o;

        var refundInvoice = await GetRefundInvoiceAsync(id);
        if (refundInvoice is not null)
            InvoiceTriggerProvider.AddInvoiceToModel(model, refundInvoice, linkGenerator);

        if (TryGetPullPaymentLinkBaseUrl(blob, refundInvoice, out var baseUrl))
            o["Link"] = linkGenerator.PullPaymentLink(id, baseUrl);

        return model;
    }

    private bool TryGetPullPaymentLinkBaseUrl(PullPaymentBlob blob, InvoiceEntity? refundInvoice, out RequestBaseUrl baseUrl)
    {
        if (blob.RequestBaseUrl is not null && RequestBaseUrl.TryFromUrl(blob.RequestBaseUrl, out baseUrl))
            return true;
        if (refundInvoice is not null &&
            !string.IsNullOrEmpty(refundInvoice.ServerUrl) &&
            RequestBaseUrl.TryFromUrl(refundInvoice.ServerUrl, out baseUrl))
            return true;
        return RequestBaseUrl.TryFromUrl(serverSettings.Settings.BaseUrl ?? "", out baseUrl);
    }

    protected override StoreWebhookEvent? GetWebhookEvent(PullPaymentEvent pullPaymentEvent)
    {
        var webhookEvt = pullPaymentEvent.Type switch
        {
            PullPaymentEvent.PullPaymentEventType.Created => new WebhookPullPaymentEvent(WebhookEventType.PullPaymentCreated, pullPaymentEvent.PullPayment.StoreId),
            PullPaymentEvent.PullPaymentEventType.Archived => new WebhookPullPaymentEvent(WebhookEventType.PullPaymentArchived, pullPaymentEvent.PullPayment.StoreId),
            _ => null
        };
        if (webhookEvt is null)
            return null;
        webhookEvt.PullPaymentId = pullPaymentEvent.PullPayment.Id;
        return webhookEvt;
    }

    protected override async Task BeforeSending(EmailRuleMatchContext context, WebhookTriggerContext<PullPaymentEvent> webhookTriggerContext)
    {
        if (await GetRefundInvoiceAsync(webhookTriggerContext.Event.PullPayment.Id) is not { } refundInvoice)
            return;
        if (InvoiceTriggerProvider.GetMailboxAddress(refundInvoice.Metadata) is { } mb &&
            context.MatchedRule.GetBTCPayAdditionalData()?.CustomerEmail is true)
        {
            context.To.Insert(0, mb);
        }
    }

    private async Task<InvoiceEntity?> GetRefundInvoiceAsync(string pullPaymentId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var refund = await ctx.Refunds
            .Include(r => r.InvoiceData)
            .FirstOrDefaultAsync(r => r.PullPaymentDataId == pullPaymentId);
        return refund?.InvoiceData is null ? null : refund.InvoiceData.GetBlob();
    }
}
