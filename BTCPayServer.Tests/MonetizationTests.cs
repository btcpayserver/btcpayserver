#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Plugins.Monetization;
using BTCPayServer.Services;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests;

public class MonetizationTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanMonetizeServer()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        await s.StartAsync();
        await s.RegisterNewUser(true);
        await CreateUser(s, "old-guest@gmail.com");

        await s.CreateNewStore();
        await GoToMonetization(s);
        await s.ClickPagePrimary();
        await s.ConfirmModal();

        await s.FindAlertMessage(partialText: "Monetization activated");

        // Creating a new user, should create a new subscriber
        var ev = await s.Server.WaitForEvent<SubscriptionEvent.NewSubscriber>(async () =>
        {
            await CreateUser(s, "new-guest@gmail.com");
        });
        Assert.Equal("new-guest@gmail.com", ev.Subscriber.Customer.Email.Get());

        // Old guest should still be able to log... they didn't migrated.
        await CanLog(s, "old-guest@gmail.com");
        await CanLog(s, "new-guest@gmail.com", "You must have a confirmed email to log in.");
        await AssertSubscribed(s, "new-guest@gmail.com", true);
        await AssertSubscribed(s, "old-guest@gmail.com", false);

        // When migrating users, only old-guest should be migrated.
        // Not the admin, and not the new guest, who is already migrated.
        await ClickSetupOffering(s);
        await s.Page.ClickAsync("#migrate-users-button");
        await s.ConfirmModal();
        await s.FindAlertMessage(partialText: "1 users migrated to the plan");
        await AssertSubscribed(s, "old-guest@gmail.com", true);
        await AssertSubscribed(s, "new-guest@gmail.com", true);

        // Now, let's try to demonetize the server and remigrate users
        await Demonetize(s);
        await s.ClickPagePrimary();
        await s.Page.SetCheckedAsync("#ActivateModal_MigrateExistingUsers", true);
        await s.ConfirmModal();
        await s.FindAlertMessage(partialText: "(2 migrated users)");
        await AssertSubscribed(s, "old-guest@gmail.com", true);
        await AssertSubscribed(s, "new-guest@gmail.com", true);

        // Now, let's try to demonetize the server and remigrate users, on a new store!
        await Demonetize(s);
        await CreateUser(s, "old2-guest@gmail.com");
        var (_, storeId) = await s.CreateNewStore();
        await GoToMonetization(s);
        await s.ClickPagePrimary();
        await s.Page.SelectOptionAsync("#ActivateModal_SelectedStoreId", storeId);
        await s.Page.SetCheckedAsync("#ActivateModal_MigrateExistingUsers", true);
        await s.ConfirmModal();
        // Normally, old2-guest, new-guest and old-guest should be migrated.
        await s.FindAlertMessage(partialText: "(3 migrated users)");
        await AssertSubscribed(s, "old2-guest@gmail.com", true);
        await AssertSubscribed(s, "old-guest@gmail.com", true);
        await AssertSubscribed(s, "new-guest@gmail.com", true);

        // Setup the server's email
        await s.Page.ClickAsync("text=Configure server email settings");
        await s.Page.ClickAsync("#server-email-collapse a");
        await new PMO.ConfigureEmailPMO(s).FillMailPit();
        await GoToMonetization(s);
        // Normally, the store's email should be set up, as the server email is set as fallback.
        await Expect(s.Page.Locator(".icon-checkmark")).ToHaveCountAsync(3);
        var offeringPMO = await GoToOffering(s);
        await offeringPMO.AssertActiveSubscribers(3);

        await using (await s.SwitchPage())
        {
            await s.GoToUrl("/");
            await s.Page.ClickAsync("#Register");
            await s.Page.FillAsync("#emailInput", "normal-guest@gmail.com");

            var newEmail = await s.Server.AssertHasEmail(async () =>
            {
                await s.ClickPagePrimary();
            });
            Assert.Equal("Confirm your email address", newEmail.Subject);
            await s.ClickOnEmailLink(newEmail);
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Info, partialText: "Your email has been confirmed. Please set your password.");
            await s.Page.FillAsync("#Password", s.Password);
            await s.Page.FillAsync("#ConfirmPassword", s.Password);
            await s.ClickPagePrimary();
        }
        await s.FastReloadAsync();
        await offeringPMO.AssertActiveSubscribers(4);

        // What happen if normal-guest reaches end of the trial period?
        await offeringPMO.GoToSubscribers();
        await offeringPMO.ToggleTestSubscriber("normal-guest@gmail.com");
        await s.FindAlertMessage(partialText: "Subscriber normal-guest@gmail.com is now test");

        var offeringUrl = s.Page.Url;
        await s.GoToStore();
        await s.AddDerivationScheme();
        await s.GoToUrl(offeringUrl);

        await using (var portal = await offeringPMO.GoToPortal("normal-guest@gmail.com"))
        {
            await portal.GoToNextPhase();
            await using (await s.SwitchPage())
            {
                // Login should redirect to the portal
                await s.GoToUrl("/");
                await s.LogIn("normal-guest@gmail.com");
                await portal.AssertCallToAction(SubscriptionTests.PortalPMO.CallToAction.Danger);
                await portal.ClickCallToAction();

                await s.Server.WaitForEvent<SubscriptionEvent.SubscriberActivated>(async () =>
                {
                    await s.PayInvoice(mine: true, clickRedirect: false);
                });
                await s.Page.ClickAsync("#StoreLink");
                await s.FindAlertMessage();

                await s.FastReloadAsync();
                await s.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await portal.AssertNoCallToAction();

                await s.GoToUrl("/");
                await s.LogIn("normal-guest@gmail.com");
                await s.CreateNewStore();
            }
        }

        // Now, the admin decides to remove can-access form plan. This should cut off access
        await offeringPMO.GoToPlans();
        var edit = await offeringPMO.Edit("Starter Plan");
        edit.DisableFeatures = ["can-access"];

        var lockoutUpdated = await s.Server.WaitForEvent<MonetizationHostedService.MonetizationLockoutUpdated>(async () =>
        {
            await edit.Save();
        });
        Assert.Equal(4, lockoutUpdated.Updated.Length);
        Assert.All(lockoutUpdated.Updated, (o) => Assert.True(o.LockoutEnabled));

        // Subscribers aren't suspended... they have a valid subscription, just not one allowing access.
        await s.FastReloadAsync();
        await offeringPMO.AssertActiveSubscribers(4);
        await using (await s.SwitchPage())
        {
            // Should not be able to login anymore
            await s.GoToUrl("/");
            await s.LogIn("normal-guest@gmail.com");
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Warning, partialText: "Your plan does not allow you to log in.");
        }

        // Now, the admin creates a new plan and allows users to upgrade.
        var add = await offeringPMO.AddPlan();
        add.PlanName = "Pro Plan";
        add.Price = "100";
        add.EnableFeatures = ["can-access"];
        await add.Save();
        edit = await offeringPMO.Edit("Starter Plan");
        edit.PlanChanges = [SubscriptionTests.AddEditPlanPMO.PlanChangeType.Upgrade];
        await edit.Save();

        // The user should be proposed to upgrade
        await using (await s.SwitchPage())
        {
            await s.GoToUrl("/");
            await s.LogIn("normal-guest@gmail.com");
            await s.ClickPagePrimary();
            await s.Server.WaitForEvent<SubscriptionEvent.PlanStarted>(async () =>
            {
                await s.PayInvoice(mine: true, clickRedirect: true);
            });

            // And after payment, he should be able to log in again
            await s.GoToUrl("/");
            await s.LogIn("normal-guest@gmail.com");
            await s.GoToProfile("ManageBilling");
            var portal = new SubscriptionTests.PortalPMO(s, null);
            await portal.AssertPlan("Pro Plan");
            await portal.AssertNoCallToAction();
        }

        await offeringPMO.GoToSubscribers();
        await s.Server.WaitForEvent<MonetizationHostedService.MonetizationLockoutUpdated>(async () =>
        {
            await offeringPMO.Suspend("normal-guest@gmail.com", "You are banned!");
        });

        // The user should be banned, unable to connect to the portal.
        await using (await s.SwitchPage())
        {
            await s.GoToUrl("/");
            await s.LogIn("normal-guest@gmail.com");
            var portal = new SubscriptionTests.PortalPMO(s, null);
            await portal.AssertCallToAction(SubscriptionTests.PortalPMO.CallToAction.Danger, noticeTitle: "Access suspended");
        }

        // Now, the admin deletes a user; this should delete the subscriber too
        await s.GoToServer(ServerNavPages.Users);
        var users = new PMO.UsersPMO(s);
        await users.DeleteUser("normal-guest@gmail.com");

        await GoToMonetization(s);
        await GoToOffering(s);
        await offeringPMO.GoToSubscribers();
        await offeringPMO.AssertHasNotSubscriber("normal-guest@gmail.com");
    }

    private async Task<SubscriptionTests.OfferingPMO> GoToOffering(PlaywrightTester s)
    {
        await s.Page.ClickAsync("#go-to-offering");
        return new SubscriptionTests.OfferingPMO(s);
    }

    private async Task AssertSubscribed(PlaywrightTester s, string email, bool isSubscribed)
    {
        var settings = s.Server.PayTester.GetService<SettingsRepository>();
        var monetization = (await settings.GetSettingAsync<MonetizationSettings>() ?? new());
        var facto = s.Server.PayTester.GetService<ApplicationDbContextFactory>();
        await using var ctx = facto.CreateContext();
        var subscriber = await ctx.Subscribers.GetBySelector(monetization.OfferingId, CustomerSelector.ByEmail(email));
        if (isSubscribed)
            Assert.NotNull(subscriber);
        else
            Assert.Null(subscriber);
    }

    private static async Task Demonetize(PlaywrightTester s)
    {
        await ClickSetupOffering(s);
        await s.Page.ClickAsync("#demonetize-button");
        await s.ConfirmModal();
        await s.FindAlertMessage(partialText: "Monetization deactivated");
    }

    private static async Task ClickSetupOffering(PlaywrightTester s)
    {
        await s.Page.ClickAsync("text=Set up the offering");
    }

    private async Task CanLog(PlaywrightTester s, string email, string? error = null)
    {
        var newPage = await s.Browser.NewPageAsync();
        await using var c = await s.SwitchPage(newPage);
        await s.GoToUrl("/");
        await s.LogIn(email, s.Password);
        if (error is not null)
        {
            Assert.Contains("/login", s.Page.Url);
            await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Warning, partialText: error);
        }
        else
            Assert.DoesNotContain("/login", s.Page.Url);
    }

    private async Task CreateUser(PlaywrightTester tester, string email)
    {
        var client = await tester.AsTestAccount().CreateClient();
        await client.CreateUser(new()
        {
            Email = email,
            Password = tester.Password
        });
    }

    private static async Task GoToMonetization(PlaywrightTester s)
    {
        await s.GoToServer("MonetizationPlugin");
    }
}
