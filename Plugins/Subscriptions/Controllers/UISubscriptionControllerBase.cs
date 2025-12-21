using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Subscriptions.Controllers;

public class UISubscriptionControllerBase(
    ApplicationDbContextFactory dbContextFactory,
    LinkGenerator linkGenerator,
    IStringLocalizer stringLocalizer,
    SubscriptionHostedService subsService) : Controller
{
    public ApplicationDbContextFactory DbContextFactory { get; } = dbContextFactory;
    public SubscriptionHostedService SubsService { get; } = subsService;
    protected IStringLocalizer StringLocalizer => stringLocalizer;
    protected LinkGenerator LinkGenerator => linkGenerator;
    public RedirectResult RedirectToInvoiceCheckout(string invoiceId) => Redirect(linkGenerator.InvoiceCheckoutLink(invoiceId, Request.GetRequestBaseUrl()));

    public IActionResult RedirectToSubscriberPortal(string portalId, string anchor = null)
        => RedirectToAction(nameof(UISubscriberPortalController.SubscriberPortal), "UISubscriberPortal", new { portalSessionId = portalId, anchor });

    public IActionResult RedirectToPlanCheckout(string checkoutId)
        => RedirectToAction(nameof(UIPlanCheckoutController.PlanCheckout), "UIPlanCheckout", new { checkoutId });

    public async Task<IActionResult> RedirectToPlanCheckoutPayment(string checkoutId, CancellationToken cancellationToken)
    {
        try
        {
            await SubsService.ProceedToSubscribe(checkoutId, cancellationToken);
        }
        catch (InvalidOperationException) { }
        catch (BitpayHttpException ex)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Html = ex.Message.Replace("\n", "<br />", StringComparison.OrdinalIgnoreCase),
                Severity = StatusMessageModel.StatusSeverity.Error,
                AllowDismiss = true
            });
            return RedirectToPlanCheckout(checkoutId);
        }

        await using var ctx = DbContextFactory.CreateContext();
        var checkout = await ctx.PlanCheckouts.GetCheckout(checkoutId);
        return checkout switch
        {
            { PlanStarted: true } when checkout.GetRedirectUrl() is string url => Redirect(url),
            { InvoiceId: { } invoiceId } => RedirectToInvoiceCheckout(invoiceId),
            _ => NotFound()
        };
    }
}
