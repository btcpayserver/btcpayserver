#nullable enable
using System;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Models;
using BTCPayServer.Services;
using BTCPayServer.Views.UIStoreMembership;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Subscriptions.Controllers;

[AllowAnonymous]
[AutoValidateAntiforgeryToken]
[Area(SubscriptionsPlugin.Area)]
[Route("subscriber-portal/{portalSessionId}")]
public class UISubscriberPortalController(
    ApplicationDbContextFactory dbContextFactory,
    LinkGenerator linkGenerator,
    IStringLocalizer stringLocalizer,
    SubscriptionHostedService subsService,
    DisplayFormatter displayFormatter,
    UriResolver uriResolver) : UISubscriptionControllerBase(dbContextFactory, linkGenerator, stringLocalizer, subsService)
{
    [HttpGet]
    public async Task<IActionResult> SubscriberPortal(string portalSessionId, string? checkoutPlanId = null, string? anchor = null, CancellationToken cancellationToken = default)
    {
        await using var ctx = DbContextFactory.CreateContext();
        if (checkoutPlanId is not null)
        {
            var checkout = await ctx.PlanCheckouts.GetCheckout(checkoutPlanId);

            var message = checkout switch
            {
                { PlanStarted: true, IsTrial: true } => StringLocalizer["The trial has started."],
                {
                    PlanStarted: false,
                    Invoice: { Status: Data.InvoiceData.Settled },
                    Plan: { OptimisticActivation: false }
                } => StringLocalizer["Payment received, waiting for confirmation..."],
                { PlanStarted: true, RefundAmount: { } refund, Plan: { Currency: { } currency } }
                    => StringLocalizer["The plan has been started. ({0} has been refunded)", displayFormatter.Currency(refund, currency)],
                { PlanStarted: true } => StringLocalizer["The plan has been started."],
                _ => null
            };

            if (message is not null)
                TempData.SetStatusSuccess(message);
            return RedirectToAction(nameof(SubscriberPortal), new { portalSessionId });
        }


        var session = await ctx.PortalSessions.GetActiveById(portalSessionId);
        var store = session?.GetStoreData();
        if (session is null || store is null)
            return NotFound();

        var planChanges = session.Subscriber.Plan.PlanChanges
            .Select(p => new SubscriberPortalViewModel.PlanChange(p.PlanChange)
            {
                ChangeType = p.Type
            })
            .ToList();
        planChanges.Add(new(session.Subscriber.Plan)
        {
            Current = true
        });
        planChanges = planChanges.OrderBy(x => x switch
        {
            { ChangeType: PlanChangeData.ChangeType.Downgrade } => 0,
            { Current: true } => 1,
            _ => 2
        }).ThenBy(x => x.Price).ThenBy(x => x.Name).ToList();

        var curr = session.Subscriber.Plan.Currency;
        var refundValue = session.Subscriber.GetUnusedPeriodAmount() ?? 0m;

        var vm = new SubscriberPortalViewModel(session)
        {
            StoreName = store.StoreName,
            StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, store.GetStoreBlob()),
            PlanChanges = planChanges,
            Refund = (refundValue, displayFormatter.Currency(refundValue, curr, DisplayFormatter.CurrencyFormat.Symbol)),
            Url = string.IsNullOrEmpty(store.StoreWebsite) ? Request.GetRequestBaseUrl().ToString() : store.StoreWebsite,
            BTCPayLogo = Url.Content("~/img/btcpay-logo.svg")
        };
        var creditHist = await ctx.SubscriberCreditHistory
            .Where(s => s.SubscriberId == session.SubscriberId && s.Currency == session.Subscriber.Plan.Currency)
            .OrderByDescending(s => s.CreatedAt)
            .Take(50)
            .ToArrayAsync(cancellationToken);
        foreach (var hist in creditHist)
        {
            string desc = HtmlEncoder.Default.Encode(hist.Description);
            desc = AddInvoiceLink(desc);
            var histVm = new SubscriberPortalViewModel.BalanceTransactionViewModel
            (
                hist.CreatedAt,
                new HtmlString(desc),
                hist.Credit - hist.Debit,
                hist.Balance
            );
            vm.Transactions.Add(histVm);
        }

        var credit = session.Subscriber.GetCredit();
        var displayFormat = DisplayFormatter.CurrencyFormat.Symbol;
        vm.MigratePopups = new();
        foreach (var change in session.Subscriber.Plan.PlanChanges)
        {
            if (change.PlanChange.Currency != curr)
                continue;

            var run = change.PlanChange.Price;
            run -= refundValue;
            var usedCredit = Math.Min(credit, run);
            usedCredit = Math.Max(usedCredit, 0);
            run -= usedCredit;

            var due = run;
            due = Math.Max(due, 0);

            var creditBalanceAdj = -usedCredit;
            creditBalanceAdj += Math.Max(0, refundValue - change.PlanChange.Price);


            var ajdText = creditBalanceAdj is 0m ? null : displayFormatter.Currency(creditBalanceAdj, curr, displayFormat);
            ajdText = ajdText is null ? null : (creditBalanceAdj > 0 ? "+" : "") + ajdText;

            var popup = new SubscriberPortalViewModel.MigratePopup()
            {
                Cost = displayFormatter.Currency(change.PlanChange.Price, curr, displayFormat),
                UsedCredit = usedCredit is 0m ? null : "-" + displayFormatter.Currency(usedCredit, curr, displayFormat),
                AmountDue = displayFormatter.Currency(due, curr, displayFormat),
                CreditBalanceAdjustment = (ajdText, creditBalanceAdj),
            };

            vm.MigratePopups.Add(change.PlanChangeId, popup);
        }

        vm.Anchor = anchor;
        return View(vm);
    }

    Regex invoiceIdRegex = new(@"\(Inv: ([^\)]*)\)", RegexOptions.Compiled);

    private string AddInvoiceLink(string desc)
    {
        var match = invoiceIdRegex.Match(desc);
        if (!match.Success)
            return desc;
        var invoiceId = match.Groups[1].Value;
        var link = LinkGenerator.ReceiptLink(invoiceId, Request.GetRequestBaseUrl());
        return invoiceIdRegex.Replace(desc, $"(Inv: <a href=\"{link}\">$1</a>)");
    }

    [HttpPost]
    public async Task<IActionResult> SubscriberPortal(string portalSessionId, SubscriberPortalViewModel vm, string command,
        string? changedPlanId = null,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var session = await ctx.PortalSessions.GetActiveById(portalSessionId);
        if (session is null)
            return NotFound();
        if (command == "add-credit")
        {
            var value = vm.Credit?.InputAmount;
            if (value is null || value.Value <= 0)
                ModelState.AddModelError("Credit.InputAmount", StringLocalizer["Please enter a positive amount"]);
            if (!ModelState.IsValid)
                return await SubscriberPortal(portalSessionId, cancellationToken: cancellationToken);

            try
            {
                var invoiceId = await SubsService.CreateCreditCheckout(session.Id, value);
                if (invoiceId is not null)
                {
                    return RedirectToInvoiceCheckout(invoiceId);
                }
            }
            catch (BitpayHttpException ex)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Html = ex.Message.Replace("\n", "<br />", StringComparison.OrdinalIgnoreCase),
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    AllowDismiss = true
                });
                return RedirectToPlanCheckout(portalSessionId);
            }
        }
        else if (command is "migrate" or "pay")
        {
            var onPay = command == "migrate" ? PlanCheckoutData.OnPayBehavior.HardMigration : PlanCheckoutData.OnPayBehavior.SoftMigration;
            if (command == "migrate" && changedPlanId is null)
            {
                if (session.Subscriber.Plan.PlanChanges.Count == 1)
                    changedPlanId = session.Subscriber.Plan.PlanChanges[0].PlanChangeId;
                else
                    return RedirectToSubscriberPortal(portalSessionId, "plans");
            }

            var checkoutId = await SubsService.CreatePlanMigrationCheckout(session.Id, changedPlanId, onPay, Request.GetRequestBaseUrl());
            return await RedirectToPlanCheckoutPayment(checkoutId, cancellationToken);
        }
        else if (command == "update-auto-renewal")
        {
            session.Subscriber.AutoRenew = !session.Subscriber.AutoRenew;
            await ctx.SaveChangesAsync(cancellationToken);
        }

        return RedirectToSubscriberPortal(portalSessionId);
    }

    [HttpPost("~/move-time")]
    public async Task<IActionResult> MoveTime(string portalSessionId, string? command = null)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var portal = await ctx.PortalSessions.GetActiveById(portalSessionId);
        if (portal is null || !portal.Subscriber.TestAccount)
            return NotFound();


        var selector = new SubscriptionHostedService.MemberSelector.Single(portal.SubscriberId);
        if (command == "reminder" && portal.Subscriber.ReminderDate is { } reminderDate)
        {
            await SubsService.MoveTime(selector, reminderDate - DateTimeOffset.UtcNow);
            TempData.SetStatusSuccess("Moved to reminder");
        }

        else if (command == "move7days")
        {
            await SubsService.MoveTime(selector, TimeSpan.FromDays(7.0));
            TempData.SetStatusSuccess("Moved 7 days");
        }
        else
        {
            if (portal.Subscriber.Phase == SubscriberData.PhaseTypes.Trial)
            {
                await SubsService.MoveTime(portal.SubscriberId, SubscriberData.PhaseTypes.Normal);
                TempData.SetStatusSuccess("Moved to normal phase");
            }
            else if (portal.Subscriber.Phase == SubscriberData.PhaseTypes.Normal)
            {
                if (portal.Subscriber.PeriodEnd is not null)
                    await SubsService.MoveTime(portal.SubscriberId, SubscriberData.PhaseTypes.Grace);
                TempData.SetStatusSuccess("Moved to grace phase");
            }
            else if (portal.Subscriber.Phase == SubscriberData.PhaseTypes.Grace)
            {
                if (portal.Subscriber.PeriodEnd is not null)
                    await SubsService.MoveTime(portal.SubscriberId, SubscriberData.PhaseTypes.Expired);
                TempData.SetStatusSuccess("Moved to expired phase");
            }
        }

        return RedirectToSubscriberPortal(portalSessionId);
    }
}
