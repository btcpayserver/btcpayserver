#nullable enable
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Services;
using BTCPayServer.Views.UIStoreMembership;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Subscriptions.Controllers;


[AllowAnonymous]
[AutoValidateAntiforgeryToken]
[Area(SubscriptionsPlugin.Area)]
[Route("plan-checkout/{checkoutId}")]
public class UIPlanCheckoutController(
    ApplicationDbContextFactory dbContextFactory,
    LinkGenerator linkGenerator,
    UriResolver uriResolver,
    IStringLocalizer stringLocalizer,
    SubscriptionHostedService subsService)
    : UISubscriptionControllerBase(dbContextFactory, linkGenerator, stringLocalizer, subsService)
{
    [HttpGet]
    public async Task<IActionResult> PlanCheckout(string checkoutId)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var checkout = await ctx.PlanCheckouts.GetCheckout(checkoutId);
        var plan = checkout?.Plan;
        if (plan is null || checkout is null)
            return NotFound();
        string? prefilledEmail = null;
        if (checkout.Subscriber is not null)
            prefilledEmail = checkout.Subscriber.Customer.Email.Get();
        else if (checkout.NewSubscriberEmail is not null)
            prefilledEmail = checkout.NewSubscriberEmail;
        var vm = new PlanCheckoutViewModel()
        {
            CheckoutId = checkoutId,
            StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, plan.Offering.App.StoreData.GetStoreBlob()),
            StoreName = plan.Offering.App.StoreData.StoreName,
            Title = plan.Name,
            Data = plan,
            Email = prefilledEmail,
            IsPrefilled = prefilledEmail?.IsValidEmail() is true,
            IsTrial = checkout.IsTrial
        };
        return View(vm);
    }

    [HttpGet("~/plan-checkout/default-redirect")]
    public async Task<IActionResult> PlanCheckoutDefaultRedirect(string? checkoutPlanId = null)
    {
        if (checkoutPlanId is null)
            return NotFound();
        await using var ctx = DbContextFactory.CreateContext();
        var checkout = await ctx.PlanCheckouts.GetCheckout(checkoutPlanId);
        if (checkout is null)
            return NotFound();

        return View(new PlanCheckoutDefaultRedirectViewModel(checkout)
        {
            StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, checkout.Plan.Offering.App.StoreData.GetStoreBlob()),
            StoreName = checkout.Plan.Offering.App.StoreData.StoreName,
        });
    }

    [HttpPost]
    public async Task<IActionResult> PlanCheckout(string checkoutId, PlanCheckoutViewModel vm,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var checkout = await ctx.PlanCheckouts.GetCheckout(checkoutId);
        if (checkout is null)
            return NotFound();
        var checkoutInvoice = checkout.InvoiceId is null ? null : await ctx.Invoices.FindAsync([checkout.InvoiceId], cancellationToken);
        if (checkoutInvoice is not null)
        {
            var status = checkoutInvoice.GetInvoiceState().Status;

            if (status is InvoiceStatus.Settled && checkout.GetRedirectUrl() is string url)
                return Redirect(url);
            if (status is not (InvoiceStatus.Expired or InvoiceStatus.Invalid))
                return RedirectToInvoiceCheckout(checkoutInvoice.Id);
        }

        var subscriber = checkout.Subscriber;
        CustomerSelector? customerSelector = null;
        if (subscriber is null)
        {
            if (checkout.NewSubscriberEmail is null)
            {
                if (!vm.Email.IsValidEmail())
                    ModelState.AddModelError(nameof(vm.Email), "Invalid email format");
                if (!ModelState.IsValid)
                    return await PlanCheckout(checkoutId);

                checkout.NewSubscriberEmail = vm.Email;
                await ctx.SaveChangesAsync();
            }

            customerSelector = CustomerSelector.ByEmail(checkout.NewSubscriberEmail);
            if (checkout.NewSubscriber)
            {
                var sub = await ctx.Subscribers.GetBySelector(checkout.Plan.OfferingId, customerSelector);
                if (sub is not null)
                {
                    ModelState.AddModelError(nameof(vm.Email), "This email already has a subscription to this offering");
                    return await PlanCheckout(checkoutId);
                }
            }
        }
        else
        {
            ModelState.Remove(nameof(vm.Email));
        }
        return await RedirectToPlanCheckoutPayment(checkoutId, cancellationToken);
    }
}
