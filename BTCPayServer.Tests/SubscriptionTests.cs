#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Events;
using BTCPayServer.Plugins;
using BTCPayServer.Tests.PMO;
using Microsoft.Playwright;
using NBitcoin;
using NBXplorer;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class SubscriptionTests(ITestOutputHelper testOutputHelper) : UnitTestBase(testOutputHelper)
{

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanChangeOfferingEmailsSettings()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.RegisterNewUser();
        await s.CreateNewStore();

        var offering = await CreateNewSubscription(s);
        await offering.GoToMails();

        var settings = new OfferingPMO.EmailSettingsForm()
        {
            PaymentRemindersDays = 7
        };
        await offering.SetEmailsSettings(settings);
        var actual = await offering.ReadEmailsSettings();
        offering.AssertEqual(settings, actual);
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanEditOfferingAndPlans()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.RegisterNewUser();
        await s.CreateNewStore();

        await CreateNewSubscription(s);

        var offeringPMO = new OfferingPMO(s);
        var editPlan = new AddEditPlanPMO(s)
        {
            PlanName = "Test plan",
            Price = "10.00",
            TrialPeriod = "7",
            GracePeriod = "7",
            EnableEntitlements = ["transaction-limit-10000", "payment-processing-0", "email-support-0"],
            PlanChanges = [AddEditPlanPMO.PlanChangeType.Upgrade, AddEditPlanPMO.PlanChangeType.Downgrade]
        };
        await offeringPMO.AddPlan();
        await editPlan.Save();

        // Remove the other plans
        for (int i = 0; i < 3; i++)
        {
            await s.Page.GetByRole(AriaRole.Link, new() { Name = "Remove" }).Nth(1).ClickAsync();
            await s.ConfirmDeleteModal();
            await s.FindAlertMessage();
        }

        await s.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();

        editPlan = new AddEditPlanPMO(s);
        editPlan.PlanName = "Test plan new name";
        editPlan.Price = "11.00";
        editPlan.TrialPeriod = "5";
        editPlan.GracePeriod = "5";
        editPlan.Description = "Super cool plan";
        editPlan.OptimisticActivation = true;
        editPlan.EnableEntitlements = ["transaction-limit-50000", "payment-processing-1", "email-support-1"];
        editPlan.DisableEntitlements = ["transaction-limit-10000", "payment-processing-0", "email-support-0"];
        await editPlan.Save();

        await s.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();

        var expected = editPlan;
        expected.OptimisticActivation = true;

        editPlan = new AddEditPlanPMO(s);
        await editPlan.ReadFields();
        editPlan.DisableEntitlements = null;
        expected.AssertEqual(editPlan);
        await s.Page.GetByTestId("offering-link").ClickAsync();
        await offeringPMO.Configure();

        var configureOffering = new ConfigureOfferingPMO(s)
        {
            Name = "New test offering 2",
            SuccessRedirectUrl = "https://test.com/test",
            Entitlements_0__Id = "analytics-dashboard-0-2",
            Entitlements_0__ShortDescription = "Basic analytics dashboard 2",
        };
        await configureOffering.Fill();

        // Remove "analytics-dashboard-1" which is the second item
        Assert.Equal("analytics-dashboard-1", await s.Page.Locator("#Entitlements_1__Id").InputValueAsync());
        await s.Page.Locator("button[name='removeIndex']").Nth(1).ClickAsync();

        await s.ClickPagePrimary();
        await offeringPMO.Configure();

        var expectedConfigure = configureOffering;
        expectedConfigure.Entitlements_1__Id = "analytics-dashboard-x";
        expectedConfigure.Entitlements_1__ShortDescription = "Custom analytics & reporting";
        configureOffering = new ConfigureOfferingPMO(s);
        await configureOffering.ReadFields();
        expectedConfigure.AssertEqual(configureOffering);

        // Can we add "Support" back?
        await s.Page.GetByRole(AriaRole.Button, new() { Name = "Add item" }).ClickAsync();
        await s.Page.Locator("#Entitlements_14__Id").FillAsync("analytics-dashboard-1");
        await s.Page.Locator("#Entitlements_14__ShortDescription").FillAsync("Advanced analytics");
        await s.ClickPagePrimary();
        await offeringPMO.Configure();

        expectedConfigure.Entitlements_1__Id = "analytics-dashboard-1";
        expectedConfigure.Entitlements_1__ShortDescription = "Advanced analytics";
        configureOffering = new ConfigureOfferingPMO(s);
        await configureOffering.ReadFields();
        expectedConfigure.AssertEqual(configureOffering);
        await s.ClickPagePrimary();

        await offeringPMO.Configure();
        await s.Page.GetByText("Delete this offering").ClickAsync();
        await s.Page.GetByRole(AriaRole.Textbox, new() { Name = "Confirm the action by typing" }).FillAsync("DELETE");
        await s.Page.ClickAsync("#ConfirmContinue");
        await s.FindAlertMessage(partialText: "App deleted");


        await CreateNewSubscription(s);

        // Change the planchanges
        editPlan = await offeringPMO.Edit("Basic Plan");
        await editPlan.ReadFields();
        editPlan.PlanChanges = [AddEditPlanPMO.PlanChangeType.Downgrade, AddEditPlanPMO.PlanChangeType.Upgrade];
        await editPlan.Save();

        expected = editPlan;
        editPlan = await offeringPMO.Edit("Basic Plan");
        await editPlan.ReadFields();
        expected.AssertEqual(editPlan);
        editPlan.PlanChanges = [AddEditPlanPMO.PlanChangeType.None, AddEditPlanPMO.PlanChangeType.Upgrade];
        await editPlan.Save();

        expected = editPlan;
        editPlan = await offeringPMO.Edit("Basic Plan");
        await editPlan.ReadFields();
        expected.AssertEqual(editPlan);
    }

    private static async Task<OfferingPMO> CreateNewSubscription(PlaywrightTester s)
    {
        await s.Page.GetByRole(AriaRole.Link, new() { Name = "Subscriptions" }).ClickAsync();
        await s.Page.GetByRole(AriaRole.Textbox, new() { Name = "Name *" }).FillAsync("New test offering");
        await s.Page.GetByRole(AriaRole.Button, new() { Name = "Create fake offering" }).ClickAsync();
        return new(s);
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanUpgradeAndDowngrade()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.RegisterNewUser();
        (_, string storeId) = await s.CreateNewStore();
        await s.AddDerivationScheme();

        var invoice = new InvoiceCheckoutPMO(s);
        var offering = await CreateNewSubscription(s);
        await offering.NewSubscriber("Enterprise Plan", "enterprise@example.com", true);
        await offering.GoToSubscribers();
        await using (var portal = await offering.GoToPortal("enterprise@example.com"))
        {
            await portal.Downgrade("Pro Plan");
            await invoice.AssertContent(new()
            {
                TotalFiat = "$99.00"
            });
        }

        await using (var portal = await offering.GoToPortal("enterprise@example.com"))
        {
            await portal.ClickCallToAction();
            await s.Server.WaitForEvent<SubscriptionEvent.SubscriberCredited>(async () =>
            {
                await s.PayInvoice(mine: true);
            });
            await invoice.ClickRedirect();
            await s.FindAlertMessage(partialText: "The plan has been started.");
            // Note that at this point, the customer has a period of 15 days + 1 month.
            // This is because the trial period is 15 days, so we extend the plan.
            await portal.GoTo7Days();
            await portal.Downgrade("Pro Plan");

            decimal totalRefunded = 0m;
            // The downgrade can be paid by the current, more expensive plan.
            var unused = GetUnusedPeriodValue(usedDays: 7, planPrice: 299.0m, daysInPeriod: 15 + DaysInThisMonth());
            totalRefunded += await portal.AssertRefunded(unused);
            var expectedBalance = totalRefunded - 99.0m;
            await portal.AssertCredit(creditBalance: $"${expectedBalance:F2}");

            // This time, we should have 1 month in the current period.
            await portal.GoTo7Days();

            var credited = await s.Server.WaitForEvent<SubscriptionEvent.SubscriberCredited>(async () =>
            {
                await portal.Downgrade("Basic Plan");
                unused = GetUnusedPeriodValue(usedDays: 7, planPrice: 99.0m, daysInPeriod: DaysInThisMonth());
                totalRefunded += await portal.AssertRefunded(unused);
            });

            Assert.Equal(unused, credited.Amount);
            Assert.Equal(unused + expectedBalance, credited.Total);


            expectedBalance = totalRefunded - 29.0m - 99.0m;
            await portal.AssertCredit("$29.00", "-$29.00", "$0.00", $"${expectedBalance:F2}");
            // The balance should now be around 202.15 USD

            // Now, let's try upgrade. Since we have enough money, we should be able to upgrade without invoice.
            await portal.GoTo7Days();
            await portal.Upgrade("Pro Plan");
            unused = GetUnusedPeriodValue(usedDays: 7, planPrice: 29.0m, daysInPeriod: DaysInThisMonth());
            totalRefunded += await portal.AssertRefunded(unused);
            expectedBalance = totalRefunded - 29.0m - 99.0m - 99.0m;
            await portal.AssertCredit(creditBalance: $"${expectedBalance:F2}");

            // However, for going back to enterprise, we do not have enough.
            await portal.GoTo7Days();
            await portal.GoTo7Days();
            await portal.GoTo7Days();
            unused = GetUnusedPeriodValue(usedDays: 21, planPrice: 99.0m, daysInPeriod: DaysInThisMonth());
            await s.Server.WaitForEvent<SubscriptionEvent.PlanStarted>(async () =>
            {
                await portal.Upgrade("Enterprise Plan");
                await invoice.AssertContent(new()
                {
                    TotalFiat = USD(299m - expectedBalance - unused)
                });
                await s.PayInvoice(mine: true);
            });
            await invoice.ClickRedirect();
            totalRefunded += await portal.AssertRefunded(unused);
        }
    }

    private static decimal GetUnusedPeriodValue(int usedDays, decimal planPrice, int daysInPeriod)
    {
        var unused = (double)(daysInPeriod - usedDays) / (double)daysInPeriod;
        var expected = (decimal)Math.Round((double)planPrice * unused, 2);
        return expected;
    }

    private static int DaysInThisMonth()
    {
        return DateTime.DaysInMonth(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month);
    }

    private string USD(decimal val)
        => $"${val.ToString("F2", CultureInfo.InvariantCulture)}";

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanUseNonRenewableFreePlan()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.RegisterNewUser();
        await s.CreateNewStore();
        await s.AddDerivationScheme();

        var offering = await CreateNewSubscription(s);
        var addPlan = await offering.AddPlan();
        addPlan.Price = "0";
        addPlan.PlanName = "Free Plan";
        addPlan.Renewable = false;
        addPlan.OptimisticActivation = true;
        addPlan.PlanChanges =
        [
            AddEditPlanPMO.PlanChangeType.Upgrade,
            AddEditPlanPMO.PlanChangeType.None,
            AddEditPlanPMO.PlanChangeType.None,
        ];
        await addPlan.Save();

        await offering.NewSubscriber("Free Plan", "free@example.com", false, hasInvoice: false);
        await offering.GoToSubscribers();
        await using (var portal = await offering.GoToPortal("free@example.com"))
        {
            await portal.GoToReminder();
            await portal.AssertCallToAction(PortalPMO.CallToAction.Warning, noticeTitle: "Upgrade needed in 3 days");
            await portal.ClickCallToAction();
            await s.PayInvoice(clickRedirect: true);

            await portal.AssertNoCallToAction();
            await portal.AssertPlan("Basic Plan");

            await portal.AssertCreditHistory([
                "Upgrade to new plan 'Basic Plan'",
                "Credit purchase",
                "Starting plan 'Free Plan'"
            ]);
        }
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanCreateSubscriberAndCircleThroughStates()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.RegisterNewUser();
        (_, string storeId) = await s.CreateNewStore();
        await s.AddDerivationScheme();

        var offering = await CreateNewSubscription(s);

        // enterprise@example.com is a trial subscriber
        await offering.NewSubscriber("Enterprise Plan", "enterprise@example.com", true);

        // basic@example.com is a basic plan subscriber (without optimistic activation), so he needs to wait confirmation
        offering.GoToPlans();
        var edit = await offering.Edit("Basic Plan");
        edit.OptimisticActivation = false;
        await edit.Save();

        await offering.NewSubscriber("Basic Plan", "basic@example.com", false);
        offering.GoToPlans();
        edit = await offering.Edit("Basic Plan");
        edit.OptimisticActivation = true;
        await edit.Save();

        // basic2@example.com is a basic plan subscriber (optimistic activation), so he is imediatly activated
        await offering.NewSubscriber("Basic Plan", "basic2@example.com", false);

        await offering.AssertHasSubscriber("enterprise@example.com", new()
        {
            Phase = SubscriberData.PhaseTypes.Trial,
            Active = OfferingPMO.ActiveState.Active
        });
        await offering.AssertHasSubscriber("basic2@example.com",
            new()
            {
                Phase = SubscriberData.PhaseTypes.Normal,
                Active = OfferingPMO.ActiveState.Active
            });
        // Payment isn't yet confirmed, and no optimistic activation
        await offering.AssertHasNotSubscriber("basic@example.com");

        // Mark the invoice of basic2 invalid, so he should go from active to inactive
        var api = await s.AsTestAccount().CreateClient();
        var invoiceId = (await api.GetInvoices(storeId)).First().Id;

        var waiting = offering.WaitEvent<SubscriptionEvent.SubscriberEvent.SubscriberDisabled>();
        await api.MarkInvoiceStatus(storeId, invoiceId, new()
        {
            Status = InvoiceStatus.Invalid
        });
        var disabled = await waiting;
        Assert.True(disabled.Subscriber.IsSuspended);
        Assert.Equal("The plan has been started by an invoice which later became invalid.", disabled.Subscriber.SuspensionReason);

        await s.Page.ReloadAsync(new() { WaitUntil = WaitUntilState.Commit });

        await offering.AssertHasSubscriber("basic2@example.com",
            new()
            {
                Phase = SubscriberData.PhaseTypes.Normal,
                Active = OfferingPMO.ActiveState.Suspended
            });

        await using (var suspendedPortal = await offering.GoToPortal("basic2@example.com"))
        {
            await suspendedPortal.AssertCallToAction(PortalPMO.CallToAction.Danger, noticeTitle: "Access suspended");
        }

        var activating = offering.WaitEvent<SubscriptionEvent.SubscriberEvent.SubscriberActivated>();
        await s.Server.GetExplorerNode("BTC").EnsureGenerateAsync(1);
        var activated = await activating;
        Assert.Equal("basic@example.com", activated.Subscriber.Customer.GetPrimaryIdentity());

        await s.Page.ReloadAsync(new() { WaitUntil = WaitUntilState.Commit });

        // Payment confirmed, this one should be active now
        await offering.AssertHasSubscriber("basic@example.com",
            new()
            {
                Phase = SubscriberData.PhaseTypes.Normal,
                Active = OfferingPMO.ActiveState.Active
            });
        await using (var portal = await offering.GoToPortal("enterprise@example.com"))
        {
            await portal.AssertCallToAction(PortalPMO.CallToAction.Info);
            await portal.ClickCallToAction();
            var changingPhase = offering.WaitEvent<SubscriptionEvent.SubscriberEvent.SubscriberPhaseChanged>();
            await s.PayInvoice(mine: true, clickRedirect: true);
            var changeEvent = await changingPhase;
            Assert.Equal(
                (SubscriberData.PhaseTypes.Normal, SubscriberData.PhaseTypes.Trial),
                (changeEvent.Subscriber.Phase, changeEvent.PreviousPhase));
            await s.Page.ReloadAsync();

            await portal.AssertNoCallToAction();

            var sendingPaymentReminder = offering.WaitEvent<SubscriptionEvent.SubscriberEvent.PaymentReminder>();
            await portal.GoToReminder();
            var paymentReminder = await sendingPaymentReminder;

            await portal.AssertCallToAction(PortalPMO.CallToAction.Warning, noticeTitle: "Payment due in 3 days");
            await portal.GoToNextPhase();

            await portal.AssertCallToAction(PortalPMO.CallToAction.Danger, noticeTitle: "Payment due");

            var disabling = offering.WaitEvent<SubscriptionEvent.SubscriberEvent.SubscriberDisabled>();
            await portal.GoToNextPhase();
            await portal.AssertCallToAction(PortalPMO.CallToAction.Danger, noticeTitle: "Access expired");
            await disabling;

            await portal.AddCredit("19.00001");
            var addingCredit = offering.WaitEvent<SubscriptionEvent.SubscriberEvent.SubscriberCredited>();
            await s.PayInvoice(mine: true, clickRedirect: true);
            var addedCredit = await addingCredit;
            Assert.Equal((19.0m, 19.0m), (addedCredit.Amount, addedCredit.Total));

            await s.Page.ReloadAsync();
            await portal.AssertCredit("$299.00", "-$19.00", "$280.00");

            addingCredit = offering.WaitEvent<SubscriptionEvent.SubscriberEvent.SubscriberCredited>();
            await portal.ClickCallToAction();
            await s.PayInvoice(mine: true, clickRedirect: true);
            addedCredit = await addingCredit;
            Assert.Equal((280.0m, 299.0m), (addedCredit.Amount, addedCredit.Total));
            await s.Page.ReloadAsync();

            await portal.AssertNoCallToAction();
        }

        await s.Page.ReloadAsync();
        await offering.Suspend("enterprise@example.com", "some reason");
        await using (var portal = await offering.GoToPortal("enterprise@example.com"))
        {
            await portal.AssertCallToAction(PortalPMO.CallToAction.Danger, noticeTitle: "Access suspended",
                noticeSubtitles: ["Your access to this subscription has been suspended.", "Reason: some reason"]);
        }

        await offering.Unsuspend("enterprise@example.com");
        await using (var portal = await offering.GoToPortal("enterprise@example.com"))
        {
            await portal.AssertNoCallToAction();
        }

        await offering.Charge("enterprise@example.com", 10.00001m, "-$10.00 (USD)");
        await offering.Credit("enterprise@example.com", 15m, "$5.00 (USD)");

        await using (var portal = await offering.GoToPortal("enterprise@example.com"))
        {
            await portal.AssertNoCallToAction();
        }

        await offering.Charge("enterprise@example.com", 5m, "$0.00 (USD)");
        await using (var portal = await offering.GoToPortal("enterprise@example.com"))
        {
            await portal.GoToReminder();
            await portal.AssertCallToAction(PortalPMO.CallToAction.Warning, noticeTitle: "Payment due in 3 days");
            await portal.ClickCallToAction();
            await s.PayInvoice(mine: true, clickRedirect: true);
            await portal.AssertNoCallToAction();
        }
    }

    class OfferingPMO(PlaywrightTester s)
    {
        public Task Configure()
            => s.Page.GetByRole(AriaRole.Link, new() { Name = "Configure" }).ClickAsync();

        public async Task<AddEditPlanPMO> AddPlan()
        {
            await s.Page.GetByRole(AriaRole.Button, new() { Name = "Add Plan" }).ClickAsync();
            return new AddEditPlanPMO(s);
        }

        public async Task NewSubscriber(string planName, string email, bool hasTrial, bool mine = false, bool? hasInvoice = null)
        {
            var allowTrial = await s.Page.Locator($"tr[data-plan-name='{planName}']").GetAttributeAsync("data-allow-trial") == "True";
            await s.Page.ClickAsync($"tr[data-plan-name='{planName}'] .dropdown-toggle");
            await s.Page.ClickAsync($"tr[data-plan-name='{planName}'] .plan-name-col a");
            Assert.Equal(hasTrial, allowTrial);
            if (allowTrial)
                await s.Page.CheckAsync("input[name='isTrial']");
            else
                Assert.False(await s.Page.Locator("input[name='isTrial']").IsVisibleAsync());
            await s.Page.ClickAsync("#newSubscriberModal button[name='command']");
            await s.Page.FillAsync("#emailInput", email);
            await s.Page.ClickAsync("button[name='command']");
            if (!allowTrial && hasInvoice is not false)
            {
                await s.PayInvoice(mine, clickRedirect: true);
            }
        }

        public async Task<AddEditPlanPMO> Edit(string planName)
        {
            await s.Page.Locator($"tr[data-plan-name='{planName}'] .edit-plan").ClickAsync();
            return new(s);
        }

        public Task GoToSubscribers()
            => s.Page.GetByRole(AriaRole.Link, new() { Name = "Subscribers" }).ClickAsync();
        public void GoToPlans()
            => s.Page.GetByRole(AriaRole.Link, new() { Name = "Plans" }).ClickAsync();
        public Task GoToMails()
            => s.Page.GetByRole(AriaRole.Link, new() { Name = "Mails" }).ClickAsync();

        public enum ActiveState
        {
            Inactive,
            Active,
            Suspended
        }

        public class ExpectedSubscriber
        {
            public SubscriberData.PhaseTypes? Phase { get; set; }
            public ActiveState? Active { get; set; }
        }

        public async Task AssertHasSubscriber(string subscriberEmail, ExpectedSubscriber? expected = null)
        {
            await s.Page.Locator(SubscriberRowSelector(subscriberEmail)).WaitForAsync();
            if (expected is not null)
            {
                if (expected.Phase is not null)
                {
                    var phase = await s.Page.Locator($"{SubscriberRowSelector(subscriberEmail)} .subscriber-phase").InnerTextAsync();
                    Assert.Equal(expected.Phase.ToString(), phase.NormalizeWhitespaces());
                }

                if (expected.Active is not null)
                {
                    var active = await s.Page.Locator($"{SubscriberRowSelector(subscriberEmail)} .status-active").InnerTextAsync();
                    Assert.Equal(expected.Active.ToString(), active.NormalizeWhitespaces());
                }
            }
        }

        private static string SubscriberRowSelector(string subscriberEmail)
        {
            return $"tr[data-subscriber-email='{subscriberEmail}']";
        }

        public async Task AssertHasNotSubscriber(string subscriberEmail)
        {
            Assert.Equal(0, await s.Page.Locator(SubscriberRowSelector(subscriberEmail)).CountAsync());
        }

        public async Task<T> WaitEvent<T>()
        {
            using var cts = new CancellationTokenSource(5000);
            var eventAggregator = s.Server.PayTester.GetService<EventAggregator>();
            return await eventAggregator.WaitNext<T>(cts.Token);
        }

        public async Task<PortalPMO> GoToPortal(string subscriberEmail)
        {
            var o = s.Page.Context.WaitForPageAsync();
            await s.Page.Locator($"{SubscriberRowSelector(subscriberEmail)} .portal-link").ClickAsync();
            var switching = await s.SwitchPage(o);
            return new(s, switching);
        }

        public async Task Suspend(string subscriberEmail, string? reason = null)
        {
            await s.Page.Locator($"{SubscriberRowSelector(subscriberEmail)} .subscriber-status").ClickAsync();
            await s.Page.Locator($"{SubscriberRowSelector(subscriberEmail)} .subscriber-status a").ClickAsync();
            if (reason is not null)
            {
                await s.Page.FillAsync("#suspensionReason", reason);
            }

            await s.Page.ClickAsync("#suspendSubscriberModal button[name='command']");
        }

        public async Task Unsuspend(string subscriberEmail)
        {
            await s.Page.Locator($"{SubscriberRowSelector(subscriberEmail)} .subscriber-status").ClickAsync();
            await s.Page.Locator($"{SubscriberRowSelector(subscriberEmail)} .subscriber-status button").ClickAsync();
        }

        public Task Charge(string subscriberEmail, decimal value, string? expectedNewTotal = null)
            => UpdateCredit(subscriberEmail, value, expectedNewTotal, "charge");

        public async Task Credit(string subscriberEmail, decimal value, string? expectedNewTotal = null)
            => await UpdateCredit(subscriberEmail, value, expectedNewTotal, "credit");

        private async Task UpdateCredit(string subscriberEmail, decimal value, string? expectedNewTotal, string action)
        {
            await s.Page.Locator($"{SubscriberRowSelector(subscriberEmail)} .subscriber-credit-col .dropdown-toggle").ClickAsync();
            await s.Page.Locator($"{SubscriberRowSelector(subscriberEmail)} .subscriber-credit-col a[data-action='{action}']").ClickAsync();
            await s.Page.FillAsync("#updateCreditModal input[name='amount']", value.ToString(CultureInfo.InvariantCulture));
            if (expectedNewTotal is not null)
                await s.Page.WaitForSelectorAsync($"#updateCreditModal .after-change:has-text('{expectedNewTotal}')");

            // Sometimes we submit by hitting Enter. Sometimes we submit by clicking the button.
            var button = s.Page.Locator($"#updateCreditModal")
                .GetByRole(AriaRole.Button, new() { Name = action == "credit" ? "Credit" : "Charge" });
            if (RandomUtils.GetInt32() % 2 == 0)
            {
                await button.ClickAsync();
            }
            else
            {
                await button.WaitForAsync();
                await s.Page.FocusAsync("#updateCreditModal input[name='amount']");
                await s.Page.Keyboard.PressAsync("Enter");
            }

            await s.FindAlertMessage(partialText: action == "charge" ? "has been charged" : "has been credited");
            var newCredit = await s.Page.Locator($"{SubscriberRowSelector(subscriberEmail)} .subscriber-credit-col").InnerTextAsync();
            if (expectedNewTotal is not null)
                Assert.Contains(expectedNewTotal.NormalizeWhitespaces(), newCredit.NormalizeWhitespaces());
        }

        public class EmailSettingsForm
        {
            public int? PaymentRemindersDays { get; set; }
        }

        public async Task SetEmailsSettings(EmailSettingsForm settings)
        {
            if (settings.PaymentRemindersDays is not null)
            {
                await s.Page.FillAsync("input[name='PaymentRemindersDays']", settings.PaymentRemindersDays.Value.ToString());
            }
            await s.ClickPagePrimary();
            await s.FindAlertMessage();
        }

        public async Task<EmailSettingsForm> ReadEmailsSettings()
        {
            var settings = new EmailSettingsForm();
            settings.PaymentRemindersDays = int.Parse(await s.Page.InputValueAsync("input[name='PaymentRemindersDays']"));
            return settings;
        }

        public void AssertEqual(EmailSettingsForm expected, EmailSettingsForm actual)
        {
            Assert.Equal(expected.PaymentRemindersDays, actual.PaymentRemindersDays);
        }
    }

    class PortalPMO(PlaywrightTester s, IAsyncDisposable disposable) : IAsyncDisposable
    {
        public async Task ClickCallToAction()
            => await s.Page.ClickAsync("div.alert-translucent button");

        public enum CallToAction
        {
            Danger,
            Warning,
            Info
        }

        public async Task AssertCallToAction(CallToAction callToAction, string? noticeTitle = null, string? noticeSubtitle = null,
            string[]? noticeSubtitles = null)
        {
            await s.Page.Locator(GetAlertSelector(callToAction)).WaitForAsync();
            if (noticeTitle is not null)
                Assert.Equal(noticeTitle.NormalizeWhitespaces(),
                    (await s.Page.Locator($"{GetAlertSelector(callToAction)} .notice-title").TextContentAsync()).NormalizeWhitespaces());
            if (noticeSubtitle is not null)
                Assert.Equal(noticeSubtitle.NormalizeWhitespaces(),
                    (await s.Page.Locator($"{GetAlertSelector(callToAction)} .notice-subtitle").TextContentAsync()).NormalizeWhitespaces());
            if (noticeSubtitles is not null)
            {
                var i = 0;
                foreach (var text in await s.Page.Locator($"{GetAlertSelector(callToAction)} .notice-subtitle").AllInnerTextsAsync())
                {
                    Assert.Equal(noticeSubtitles[i].NormalizeWhitespaces(), text.NormalizeWhitespaces());
                    i++;
                }

                Assert.Equal(i, noticeSubtitles.Length);
            }
        }

        private static string GetAlertSelector(CallToAction callToAction) => $"div.alert-translucent.alert-{callToAction.ToString().ToLowerInvariant()}";

        public async Task AssertNoCallToAction()
            => Assert.Equal(0, await s.Page.Locator($"div.alert-translucent").CountAsync());


        public ValueTask DisposeAsync() => disposable.DisposeAsync();

        public Task GoToNextPhase()
            => s.Page.ClickAsync("#MovePhase");

        public Task GoTo7Days()
            => s.Page.ClickAsync("#Move7days");

        public Task GoToReminder()
            => s.Page.ClickAsync("#MoveToReminder");

        public async Task AddCredit(string credit)
        {
            await s.Page.ClickAsync("#add-credit");
            await s.Page.FillAsync("#credit-input input", credit);
            await s.Page.ClickAsync("#credit-input button");
        }

        public async Task AssertCredit(string? planPrice = null, string? creditApplied = null, string? nextCharge = null, string? creditBalance = null)
        {
            if (planPrice is not null)
                Assert.Equal(planPrice.NormalizeWhitespaces(),
                    (await s.Page.Locator(".credit-plan-price div:nth-child(2)").TextContentAsync()).NormalizeWhitespaces());
            if (creditApplied is not null)
                Assert.Equal(creditApplied.NormalizeWhitespaces(),
                    (await s.Page.Locator(".credit-applied div:nth-child(2)").TextContentAsync()).NormalizeWhitespaces());
            if (nextCharge is not null)
                Assert.Equal(nextCharge.NormalizeWhitespaces(),
                    (await s.Page.Locator(".credit-next-charge div:nth-child(2)").TextContentAsync()).NormalizeWhitespaces());
            if (creditBalance is not null)
                Assert.Equal(creditBalance.NormalizeWhitespaces(),
                    (await s.Page.Locator(".credit-balance").TextContentAsync()).NormalizeWhitespaces());
        }

        private async Task ChangePlan(string planName, string buttonText)
        {
            await s.Page.ClickAsync($".changeplan-container[data-plan-name='{planName}'] a:has-text('{buttonText}')");
            await s.Page.ClickAsync($"#changePlanModal button[value='migrate']:has-text('{buttonText}')");
        }

        public Task Downgrade(string planName) => ChangePlan(planName, "Downgrade");

        public Task Upgrade(string planName) => ChangePlan(planName, "Upgrade");

        public async Task<decimal> AssertRefunded(decimal refunded)
        {
            var text = await (await s.FindAlertMessage()).TextContentAsync();
            var match = Regex.Match(text!, @"\((.*?) USD has been refunded\)");
            var v = decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var diff = Math.Abs(refunded - v);
            Assert.True(diff < 2.0m);
            return v;
        }

        public async Task AssertPlan(string plan)
        {
            var name = await s.Page.GetByTestId("plan-name").InnerTextAsync();
            Assert.Equal(plan, name);
        }

        public async Task AssertCreditHistory(List<string> creditLines)
        {
            var rows = await s.Page.QuerySelectorAllAsync(".credit-history tr td:nth-child(2)");
            for (int i = 0; i < creditLines.Count; i++)
            {
                var txt = await rows[i].InnerTextAsync();
                Assert.StartsWith(creditLines[i], txt);
            }
        }
    }

    class ConfigureOfferingPMO(PlaywrightTester tester)
    {
        public string? Name { get; set; }
        public string? SuccessRedirectUrl { get; set; }

        public string? Entitlements_0__Id { get; set; }
        public string? Entitlements_0__ShortDescription { get; set; }
        public string? Entitlements_1__Id { get; set; }
        public string? Entitlements_1__ShortDescription { get; set; }

        public async Task Fill()
        {
            var s = tester;
            if (Name is not null)
                await s.Page.Locator("#Name").FillAsync(Name);
            if (SuccessRedirectUrl is not null)
                await s.Page.GetByRole(AriaRole.Textbox, new() { Name = "Success redirect url" }).FillAsync(SuccessRedirectUrl);
            if (Entitlements_0__Id is not null)
                await s.Page.Locator("#Entitlements_0__Id").FillAsync(Entitlements_0__Id);
            if (Entitlements_0__ShortDescription is not null)
                await s.Page.Locator("#Entitlements_0__ShortDescription").FillAsync(Entitlements_0__ShortDescription);
            if (Entitlements_1__Id is not null)
                await s.Page.Locator("#Entitlements_1__Id").FillAsync(Entitlements_1__Id);
            if (Entitlements_1__ShortDescription is not null)
                await s.Page.Locator("#Entitlements_1__ShortDescription").FillAsync(Entitlements_1__ShortDescription);
        }

        public async Task ReadFields()
        {
            var s = tester;
            Name = await s.Page.Locator("#Name").InputValueAsync();
            SuccessRedirectUrl = await s.Page.GetByRole(AriaRole.Textbox, new() { Name = "Success redirect url" }).InputValueAsync();
            Entitlements_0__Id = await s.Page.Locator("#Entitlements_0__Id").InputValueAsync();
            Entitlements_0__ShortDescription = await s.Page.Locator("#Entitlements_0__ShortDescription").InputValueAsync();
            Entitlements_1__Id = await s.Page.Locator("#Entitlements_1__Id").InputValueAsync();
            Entitlements_1__ShortDescription = await s.Page.Locator("#Entitlements_1__ShortDescription").InputValueAsync();
        }

        public void AssertEqual(ConfigureOfferingPMO b)
        {
            Assert.Equal(Name ?? "", b.Name ?? "");
            Assert.Equal(SuccessRedirectUrl ?? "", b.SuccessRedirectUrl ?? "");
            Assert.Equal(Entitlements_0__Id ?? "", b.Entitlements_0__Id ?? "");
            Assert.Equal(Entitlements_0__ShortDescription ?? "", b.Entitlements_0__ShortDescription ?? "");
            Assert.Equal(Entitlements_1__Id ?? "", b.Entitlements_1__Id ?? "");
            Assert.Equal(Entitlements_1__ShortDescription ?? "", b.Entitlements_1__ShortDescription ?? "");
        }
    }

    class AddEditPlanPMO(PlaywrightTester tester)
    {
        public string? PlanName { get; set; }
        public string? Price { get; set; }
        public string? TrialPeriod { get; set; }
        public string? GracePeriod { get; set; }
        public string? Description { get; set; }
        public bool? OptimisticActivation { get; set; }

        public List<string>? EnableEntitlements { get; set; }
        public List<string>? DisableEntitlements { get; set; }
        public PlanChangeType[]? PlanChanges { get; set; }
        public bool? Renewable { get; set; }

        public enum PlanChangeType
        {
            Downgrade,
            Upgrade,
            None
        }

        public async Task Save()
        {
            var s = tester;
            if (PlanName is not null)
                await s.Page.GetByRole(AriaRole.Textbox, new() { Name = "Plan Name *" }).FillAsync(PlanName);
            if (Description is not null)
                await s.Page.GetByRole(AriaRole.Textbox, new() { Name = "Description", Exact = true }).FillAsync(Description);
            if (Price is not null)
                await s.Page.GetByRole(AriaRole.Textbox, new() { Name = "Price *" }).FillAsync(Price);
            if (TrialPeriod is not null)
                await s.Page.GetByRole(AriaRole.Spinbutton, new() { Name = "Trial Period (days)" }).FillAsync(TrialPeriod);
            if (GracePeriod is not null)
                await s.Page.GetByRole(AriaRole.Spinbutton, new() { Name = "Grace Period (days)" }).FillAsync(GracePeriod);

            if (PlanChanges is not null)
            {
                for (var i = 0; i < PlanChanges.Length; i++)
                {
                    await s.Page.Locator($"#PlanChanges_{i}__SelectedType").SelectOptionAsync(new[] { PlanChanges[i].ToString() });
                }
            }

            foreach (var entitlement in EnableEntitlements ?? [])
            {
                await s.Page.GetByTestId($"check_{entitlement}").CheckAsync();
            }

            foreach (var entitlement in DisableEntitlements ?? [])
            {
                await s.Page.GetByTestId($"check_{entitlement}").UncheckAsync();
            }

            if (OptimisticActivation is not null)
                await s.Page.GetByRole(AriaRole.Checkbox, new() { Name = "Optimistic activation" }).SetCheckedAsync(OptimisticActivation.Value);
            if (Renewable is not null)
                await s.Page.GetByRole(AriaRole.Checkbox, new() { Name = "Renewable" }).SetCheckedAsync(Renewable.Value);
            await s.ClickPagePrimary();
            await s.FindAlertMessage();
        }

        public async Task ReadFields()
        {
            var s = tester;
            PlanName = await s.Page.GetByRole(AriaRole.Textbox, new() { Name = "Plan Name *" }).InputValueAsync();
            Description = await s.Page.GetByRole(AriaRole.Textbox, new() { Name = "Description", Exact = true }).InputValueAsync();
            Price = await s.Page.GetByRole(AriaRole.Textbox, new() { Name = "Price *" }).InputValueAsync();
            TrialPeriod = await s.Page.GetByRole(AriaRole.Spinbutton, new() { Name = "Trial Period (days)" }).InputValueAsync();
            GracePeriod = await s.Page.GetByRole(AriaRole.Spinbutton, new() { Name = "Grace Period (days)" }).InputValueAsync();

            foreach (var entitlement in await s.Page.QuerySelectorAllAsync(".entitlement-checkbox"))
            {
                var isChecked = await entitlement.IsCheckedAsync();
                var id = (await entitlement.GetAttributeAsync("data-testid"))!.Substring(6);
                if (isChecked)
                {
                    EnableEntitlements ??= new();
                    EnableEntitlements.Add(id);
                }
                else
                {
                    DisableEntitlements ??= new();
                    DisableEntitlements.Add(id);
                }
            }

            OptimisticActivation = await s.Page.GetByRole(AriaRole.Checkbox, new() { Name = "Optimistic activation" }).IsCheckedAsync();
            Renewable = await s.Page.GetByRole(AriaRole.Checkbox, new() { Name = "Renewable" }).IsCheckedAsync();
            List<PlanChangeType> changes = new();
            foreach (var change in await s.Page.Locator(".plan-change-select").AllAsync())
            {
                changes.Add(Enum.Parse<PlanChangeType>(await change.InputValueAsync()));
            }

            PlanChanges = changes.ToArray();
        }

        public void AssertEqual(AddEditPlanPMO b)
        {
            Assert.Equal(PlanName ?? "", b.PlanName ?? "");
            Assert.Equal(Description ?? "", b.Description ?? "");
            Assert.Equal(Price ?? "", b.Price ?? "");
            Assert.Equal(TrialPeriod ?? "", b.TrialPeriod ?? "");
            Assert.Equal(GracePeriod ?? "", b.GracePeriod ?? "");

            if (EnableEntitlements is not null && b.EnableEntitlements is not null)
            {
                Assert.Equal(EnableEntitlements.Count, b.EnableEntitlements.Count);

                var (ea, eb) = (EnableEntitlements.OrderBy(e => e).ToArray(), b.EnableEntitlements.OrderBy(e => e).ToArray());
                for (int i = 0; i < EnableEntitlements.Count; i++)
                    Assert.Equal(ea[i], eb[i]);
            }

            if (DisableEntitlements is not null && b.DisableEntitlements is not null)
            {
                Assert.Equal(DisableEntitlements.Count, b.DisableEntitlements.Count);
                var (ea, eb) = (DisableEntitlements.OrderBy(e => e).ToArray(), b.DisableEntitlements.OrderBy(e => e).ToArray());
                for (int i = 0; i < DisableEntitlements.Count; i++)
                    Assert.Equal(ea[i], eb[i]);
            }

            Assert.Equal(OptimisticActivation, b.OptimisticActivation);
            if (PlanChanges is not null && b.PlanChanges is not null)
            {
                Assert.Equal(PlanChanges.Length, b.PlanChanges.Length);
                for (var i = 0; i < PlanChanges.Length; i++)
                {
                    Assert.Equal(PlanChanges[i], b.PlanChanges[i]);
                }
            }
        }
    }
}
