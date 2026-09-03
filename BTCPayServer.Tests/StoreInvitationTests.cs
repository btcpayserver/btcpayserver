using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Emails;
using BTCPayServer.Plugins.Emails.HostedServices;
using BTCPayServer.Services;
using BTCPayServer.Views.Stores;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class StoreInvitationTests(ITestOutputHelper testOutputHelper) : UnitTestBase(testOutputHelper)
{
    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanInviteExistingUserToStore()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        // The first registered user is always made a server admin, so burn that slot before
        // creating the two plain users this scenario is about.
        await s.RegisterNewUser(true);
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        var invitee = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        await s.RegisterNewUser();
        var (_, storeId) = await s.CreateNewStore();
        await s.GoToStore(storeId, StoreNavPages.Users);

        // A store owner is not offered the bypass at all.
        await Expect(s.Page.Locator(".store-users__require-invitation")).ToHaveCountAsync(0);

        // Sending an invite lands on a wizard-layout confirmation carrying the link and QR.
        var invitationUrl = string.Empty;
        var trigger = await s.Server.WaitForEvent<TriggerEvent>(async () =>
            invitationUrl = await InviteToStore(s, invitee),
            evt => evt.Trigger == StoreMailTriggers.StoreInvitePending);
        Assert.Equal(storeId, trigger.StoreId);
        Assert.Contains("/invitations/", invitationUrl);

        // The owner is not the invitee, so their own link explains itself instead of 404ing.
        await s.GoToUrl(invitationUrl);
        await Expect(s.Page.Locator(".store-invitation__wrong-user")).ToHaveCountAsync(1);
        await Expect(s.Page.Locator(".store-invitation__accept")).ToHaveCountAsync(0);
        await s.GoToStore(storeId, StoreNavPages.Users);

        // The invitee shows up in the single users table as pending, not as a member.
        var row = s.Page.Locator($"#StoreUsersList tr:has-text('{invitee}')");
        await Expect(row).ToHaveCountAsync(1);
        await Expect(row.Locator(".store-users__status")).ToContainTextAsync("Pending");

        // The invitee has no access until they accept.
        await s.Logout();
        await s.LogIn(invitee);
        await s.GoToUrl($"/stores/{storeId}");
        Assert.Contains("ReturnUrl", s.Page.Url);

        // Accepting happens on the invitation link, which uses the wizard layout.
        await s.GoToUrl(invitationUrl);
        await Expect(s.Page.Locator("#mainNav")).ToHaveCountAsync(0);
        await s.Page.ClickAsync(".store-invitation__accept");
        await s.FindAlertMessage(partialText: "You have joined");
        // Employee has no store settings permission, so acceptance lands on the store list.
        // That page must stay reachable for such a role, or joining ends in an access error.
        Assert.EndsWith("/stores", s.Page.Url);
        Assert.DoesNotContain("403", s.Page.Url);
        await s.GoToUrl($"/stores/{storeId}/invoices");
        Assert.DoesNotContain("ReturnUrl", s.Page.Url);
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanDeclineAndCancelStoreInvitations()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        await s.RegisterNewUser(true);
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        var invitee = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        var owner = await s.RegisterNewUser();
        var (_, storeId) = await s.CreateNewStore();
        await s.GoToStore(storeId, StoreNavPages.Users);
        var invitationUrl = await InviteToStore(s, invitee);

        // The invitee declines: the row disappears for the store too.
        await s.Logout();
        await s.LogIn(invitee);
        await s.GoToUrl(invitationUrl);
        await s.Page.ClickAsync(".store-invitation__decline");
        await s.FindAlertMessage(partialText: "Invitation declined");

        await s.Logout();
        await s.LogIn(owner);
        await s.GoToStore(storeId, StoreNavPages.Users);
        await Expect(s.Page.Locator($"#StoreUsersList tr:has-text('{invitee}')")).ToHaveCountAsync(0);

        // Re-invite, then the store withdraws it.
        await InviteToStore(s, invitee);
        await s.Page.ClickAsync(".store-users__cancel");
        await s.FindAlertMessage(partialText: "Invitation cancelled");
        await Expect(s.Page.Locator($"#StoreUsersList tr:has-text('{invitee}')")).ToHaveCountAsync(0);
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanResendStoreInvitation()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        await s.RegisterNewUser(true);
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        var invitee = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        await s.RegisterNewUser();
        var (_, storeId) = await s.CreateNewStore();
        await s.GoToStore(storeId, StoreNavPages.Users);
        var first = await InviteToStore(s, invitee);

        await s.Page.ClickAsync(".store-users__resend");
        await Expect(s.Page.Locator("#StoreInvitationSent")).ToHaveCountAsync(1);
        var resent = await s.Page.Locator("#InvitationUrl").GetAttributeAsync("data-text");
        Assert.NotEqual(first, resent);
        await s.Page.ClickAsync("#BackToStoreUsers");

        // Resending replaces the pending invitation, invalidating any previously shared link.
        await s.Logout();
        await s.LogIn(invitee);
        await s.GoToUrl(first, ignoreResponse: true);
        await Expect(s.Page.Locator(".store-invitation__accept")).ToHaveCountAsync(0);

        await s.GoToUrl(resent!);
        await Expect(s.Page.Locator(".store-invitation__accept")).ToHaveCountAsync(1);
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task ServerAdminCanAddUserWithoutInvitation()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        var invitee = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        await s.RegisterNewUser(true);
        var (_, storeId) = await s.CreateNewStore();
        await s.GoToStore(storeId, StoreNavPages.Users);

        // Requiring an invitation is the default, even for a server admin.
        var requireInvitation = s.Page.Locator(".store-users__require-invitation");
        await Expect(requireInvitation).ToHaveCountAsync(1);
        Assert.True(await requireInvitation.IsCheckedAsync());

        await requireInvitation.UncheckAsync();
        await s.AddUserToStore(storeId, invitee, "Guest");
        var row = s.Page.Locator($"#StoreUsersList tr:has-text('{invitee}')");
        await Expect(row.Locator(".store-users__status")).ToContainTextAsync("Active");

        // Ticking it makes even an admin go through the invitation.
        var second = await CreateOtherUser(s);
        await s.GoToStore(storeId, StoreNavPages.Users);
        await requireInvitation.CheckAsync();
        await InviteToStore(s, second, "Guest");

        // Granting membership directly while an invitation is outstanding drops the invitation,
        // so accepting it later cannot overwrite the role that was just set.
        await requireInvitation.UncheckAsync();
        await s.AddUserToStore(storeId, second, "Manager");
        var secondRow = s.Page.Locator($"#StoreUsersList tr:has-text('{second}')");
        await Expect(secondRow).ToHaveCountAsync(1);
        await Expect(secondRow.Locator(".store-users__status")).ToContainTextAsync("Active");

        // A new account keeps the server invitation flow and receives store access after signup.
        await s.GoToStore(storeId, StoreNavPages.Users);
        await s.Page.FillAsync("#Email", $"{Guid.NewGuid().ToString()[..12]}@example.com");
        await s.Page.ClickAsync("#AddUser");
        var alert = await s.FindAlertMessage(partialText: "The user has been added successfully");
        var accountInvitationUrl = await alert.Locator("a.alert-link[href*='/invite/']").GetAttributeAsync("href");
        Assert.NotNull(accountInvitationUrl);
        Assert.DoesNotContain("/invitation/", accountInvitationUrl);
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task NonAdminStoreOwnerCanCreateAndAddNewUser()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        // The first registered user is always a server admin, so create the store with a plain user.
        await s.RegisterNewUser(true);
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        await s.RegisterNewUser();
        Assert.False(s.IsAdmin);

        var (_, storeId) = await s.CreateNewStore();
        await s.GoToStore(storeId, StoreNavPages.Users);
        await Expect(s.Page.Locator(".store-users__require-invitation")).ToHaveCountAsync(0);

        var email = $"{Guid.NewGuid().ToString()[..12]}@example.com";
        await s.Page.FillAsync("#Email", email);
        await s.Page.SelectOptionAsync("#Role", "Guest");
        await s.Page.ClickAsync("#AddUser");

        var alert = await s.FindAlertMessage(partialText: "The user has been added successfully");
        var accountInvitationUrl = await alert.Locator("a.alert-link[href*='/invite/']").GetAttributeAsync("href");
        Assert.NotNull(accountInvitationUrl);
        Assert.DoesNotContain("/invitation/", accountInvitationUrl);

        var row = s.Page.Locator(".store-users__row").Filter(new() { HasText = email });
        await Expect(row).ToHaveCountAsync(1);
        await Expect(row.Locator(".store-users__status")).ToContainTextAsync("Active");
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task StoreOwnerCanSkipInvitationWhenServerAllowsIt()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        var settings = s.Server.PayTester.GetService<SettingsRepository>();
        await settings.UpdateSetting(new PoliciesSettings { AllowStoreOwnersToSkipInvitation = true });

        await s.RegisterNewUser(true);
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        var invitee = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        await s.RegisterNewUser();
        var (_, storeId) = await s.CreateNewStore();
        await s.GoToStore(storeId, StoreNavPages.Users);

        // The store owner is offered the bypass now, but inviting is still what they get by default.
        var requireInvitation = s.Page.Locator(".store-users__require-invitation");
        await Expect(requireInvitation).ToHaveCountAsync(1);
        Assert.True(await requireInvitation.IsCheckedAsync());

        await requireInvitation.UncheckAsync();
        await s.AddUserToStore(storeId, invitee, "Guest");
        var row = s.Page.Locator($"#StoreUsersList tr:has-text('{invitee}')");
        await Expect(row.Locator(".store-users__status")).ToContainTextAsync("Active");
    }

    /// <summary>Sends an invite and returns its link, leaving the browser back on the users page.</summary>
    private static async Task<string> InviteToStore(PlaywrightTester s, string email, string role = "Employee")
    {
        await s.Page.FillAsync("#Email", email);
        await s.Page.SelectOptionAsync("#Role", role);
        await s.Page.ClickAsync("#AddUser");
        await Expect(s.Page.Locator("#StoreInvitationSent")).ToHaveCountAsync(1);
        var url = await s.Page.Locator("#InvitationUrl").GetAttributeAsync("data-text");
        await s.Page.ClickAsync("#BackToStoreUsers");
        return url;
    }

    private static async Task<string> CreateOtherUser(PlaywrightTester s)
    {
        var current = s.CreatedUser;
        var password = s.Password;
        await s.Logout();
        await s.GoToRegister();
        var other = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.LogIn(current, password);
        return other;
    }
}
