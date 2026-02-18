using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class RolesTests(ITestOutputHelper testOutputHelper) : UnitTestBase(testOutputHelper)
{
    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanChangeUserRoles()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        await s.StartAsync();

        // Setup users and store
        var employee = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        var owner = await s.RegisterNewUser(true);
        var (_, storeId) = await s.CreateNewStore();
        await s.GoToStore();
        await s.AddUserToStore(storeId, employee, "Employee");

        // Should successfully change the role
        var userRows = await s.Page.Locator("#StoreUsersList tr").AllAsync();
        Assert.Equal(2, userRows.Count);
        ILocator employeeRow = null;
        foreach (var row in userRows)
        {
            if ((await row.InnerTextAsync()).Contains(employee, StringComparison.InvariantCultureIgnoreCase)) employeeRow = row;
        }

        Assert.NotNull(employeeRow);
        await employeeRow.Locator("a[data-bs-target='#EditModal']").ClickAsync();
        Assert.Equal(employee, await s.Page.InnerTextAsync("#EditUserEmail"));
        await s.Page.SelectOptionAsync("#EditUserRole", "Manager");
        await s.Page.ClickAsync("#EditContinue");
        await s.FindAlertMessage(partialText: $"The role of {employee} has been changed to Manager.");

        // Should not see a message when not changing role
        userRows = await s.Page.Locator("#StoreUsersList tr").AllAsync();
        Assert.Equal(2, userRows.Count);
        employeeRow = null;
        foreach (var row in userRows)
        {
            if ((await row.InnerTextAsync()).Contains(employee, StringComparison.InvariantCultureIgnoreCase)) employeeRow = row;
        }

        Assert.NotNull(employeeRow);
        await employeeRow.Locator("a[data-bs-target='#EditModal']").ClickAsync();
        Assert.Equal(employee, await s.Page.InnerTextAsync("#EditUserEmail"));
        await s.Page.ClickAsync("#EditContinue");
        await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error, "The user already has the role Manager.");

        // Should not change last owner
        userRows = await s.Page.Locator("#StoreUsersList tr").AllAsync();
        Assert.Equal(2, userRows.Count);
        ILocator ownerRow = null;
        foreach (var row in userRows)
        {
            if ((await row.InnerTextAsync()).Contains(owner, StringComparison.InvariantCultureIgnoreCase)) ownerRow = row;
        }

        Assert.NotNull(ownerRow);
        await ownerRow.Locator("a[data-bs-target='#EditModal']").ClickAsync();
        Assert.Equal(owner, await s.Page.InnerTextAsync("#EditUserEmail"));
        await s.Page.SelectOptionAsync("#EditUserRole", "Employee");
        await s.Page.ClickAsync("#EditContinue");
        await s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error, "The user is the last owner. Their role cannot be changed.");
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanUseRoleManager()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        await s.StartAsync();
        await s.RegisterNewUser(true);
        await s.GoToHome();
        await s.GoToServer(ServerNavPages.Roles);
        await s.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        var existingServerRoles = await s.Page.Locator("table tr").AllAsync();
        Assert.True(existingServerRoles.Count >= 5);
        async Task<ILocator> FindRoleRow(string roleName)
        {
            foreach (var roleItem in existingServerRoles)
            {
                var cells = await roleItem.Locator("td").AllAsync();
                var text = cells.Count > 0
                    ? await cells[0].TextContentAsync()
                    : await roleItem.TextContentAsync();

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                text = text.Trim();
                if (roleName.Equals("Guest", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (text.Contains("Guest", StringComparison.InvariantCultureIgnoreCase) &&
                        !text.Contains("Multisigner Guest", StringComparison.InvariantCultureIgnoreCase))
                    {
                        return roleItem;
                    }
                }
                else if (text.Contains(roleName, StringComparison.InvariantCultureIgnoreCase))
                {
                    return roleItem;
                }
            }

            return null;
        }

        var ownerRow = await FindRoleRow("Owner");
        var managerRow = await FindRoleRow("Manager");
        var employeeRow = await FindRoleRow("Employee");
        var guestRow = await FindRoleRow("Guest");

        Assert.NotNull(ownerRow);
        Assert.NotNull(managerRow);
        Assert.NotNull(employeeRow);
        Assert.NotNull(guestRow);

        var ownerBadges = await ownerRow.Locator(".badge").AllAsync();
        var ownerBadgeTexts = await Task.WhenAll(ownerBadges.Select(async element => await element.TextContentAsync()));
        Assert.Contains(ownerBadgeTexts, text => text.Equals("Default", StringComparison.InvariantCultureIgnoreCase));
        Assert.Contains(ownerBadgeTexts, text => text.Equals("Server-wide", StringComparison.InvariantCultureIgnoreCase));

        var managerBadges = await managerRow.Locator(".badge").AllAsync();
        var managerBadgeTexts = await Task.WhenAll(managerBadges.Select(async element => await element.TextContentAsync()));
        Assert.DoesNotContain(managerBadgeTexts, text => text.Equals("Default", StringComparison.InvariantCultureIgnoreCase));
        Assert.Contains(managerBadgeTexts, text => text.Equals("Server-wide", StringComparison.InvariantCultureIgnoreCase));

        var employeeBadges = await employeeRow.Locator(".badge").AllAsync();
        var employeeBadgeTexts = await Task.WhenAll(employeeBadges.Select(async element => await element.TextContentAsync()));
        Assert.DoesNotContain(employeeBadgeTexts, text => text.Equals("Default", StringComparison.InvariantCultureIgnoreCase));
        Assert.Contains(employeeBadgeTexts, text => text.Equals("Server-wide", StringComparison.InvariantCultureIgnoreCase));

        var guestBadges = await guestRow.Locator(".badge").AllAsync();
        var guestBadgeTexts = await Task.WhenAll(guestBadges.Select(async element => await element.TextContentAsync()));
        Assert.DoesNotContain(guestBadgeTexts, text => text.Equals("Default", StringComparison.InvariantCultureIgnoreCase));
        Assert.Contains(guestBadgeTexts, text => text.Equals("Server-wide", StringComparison.InvariantCultureIgnoreCase));
        await guestRow.Locator("#SetDefault").ClickAsync();
        await s.FindAlertMessage(partialText: "Role set default");

        existingServerRoles = await s.Page.Locator("table tr").AllAsync();
        ownerRow = await FindRoleRow("Owner");
        guestRow = await FindRoleRow("Guest");

        guestBadges = await guestRow.Locator(".badge").AllAsync();
        var guestBadgeTexts2 = await Task.WhenAll(guestBadges.Select(async element => await element.TextContentAsync()));
        Assert.Contains(guestBadgeTexts2, text => text.Equals("Default", StringComparison.InvariantCultureIgnoreCase));

        ownerBadges = await ownerRow.Locator(".badge").AllAsync();
        var ownerBadgeTexts2 = await Task.WhenAll(ownerBadges.Select(async element => await element.TextContentAsync()));
        Assert.DoesNotContain(ownerBadgeTexts2, text => text.Equals("Default", StringComparison.InvariantCultureIgnoreCase));
        await ownerRow.Locator("#SetDefault").ClickAsync();

        await s.FindAlertMessage(partialText: "Role set default");

        await s.CreateNewStore();
        await s.GoToStore(StoreNavPages.Roles);
        existingServerRoles = await s.Page.Locator("table tr").AllAsync();
        Assert.True(existingServerRoles.Count >= 5);
        var serverRoleTexts = await Task.WhenAll(existingServerRoles.Select(async element => await element.TextContentAsync()));
        Assert.True(serverRoleTexts.Count(text => text.Contains("Server-wide", StringComparison.InvariantCultureIgnoreCase)) >= 4);

        ownerRow = await FindRoleRow("Owner");

        await ownerRow.Locator("text=Remove").ClickAsync();
        await s.Page.WaitForLoadStateAsync();
        Assert.DoesNotContain("ConfirmContinue", await s.Page.ContentAsync());
        await s.Page.GoBackAsync();
        existingServerRoles = await s.Page.Locator("table tr").AllAsync();
        guestRow = await FindRoleRow("Guest");

        await guestRow.Locator("text=Remove").ClickAsync();
        await s.Page.ClickAsync("#ConfirmContinue");
        await s.FindAlertMessage();

        await s.GoToStore();
        await s.GoToStore(StoreNavPages.Roles);
        await s.ClickPagePrimary();

        await s.Page.Locator("#Role").FillAsync("store role");
        await s.Page.Locator("input.policy-cb").First.CheckAsync();
        await s.ClickPagePrimary();
        await s.FindAlertMessage();

        existingServerRoles = await s.Page.Locator("table tr").AllAsync();
        foreach (var roleItem in existingServerRoles)
        {
            var text = await roleItem.TextContentAsync();
            Assert.NotNull(text);
            if (text.Contains("store role", StringComparison.InvariantCultureIgnoreCase))
            {
                guestRow = roleItem;
                break;
            }
        }

        guestBadges = await guestRow.Locator(".badge").AllAsync();
        var guestBadgeTexts3 = await Task.WhenAll(guestBadges.Select(async element => await element.TextContentAsync()));
        Assert.DoesNotContain(guestBadgeTexts3, text => text.Equals("server-wide", StringComparison.InvariantCultureIgnoreCase));
        await s.GoToStore(StoreNavPages.Users);
        var options = await s.Page.Locator("#Role option").AllAsync();
        Assert.True(options.Count >= 4);
        var optionTexts = await Task.WhenAll(options.Select(async element => await element.TextContentAsync()));
        Assert.Contains(optionTexts, text => text.Equals("store role", StringComparison.InvariantCultureIgnoreCase));
        await s.CreateNewStore();
        await s.GoToStore(StoreNavPages.Roles);
        existingServerRoles = await s.Page.Locator("table tr").AllAsync();
        Assert.True(existingServerRoles.Count >= 4);
        var serverRoleTexts2 = await Task.WhenAll(existingServerRoles.Select(async element => await element.TextContentAsync()));
        Assert.True(serverRoleTexts2.Count(text => text.Contains("Server-wide", StringComparison.InvariantCultureIgnoreCase)) >= 3);
        Assert.Equal(0, serverRoleTexts2.Count(text => text.Contains("store role", StringComparison.InvariantCultureIgnoreCase)));
        await s.GoToStore(StoreNavPages.Users);
        options = await s.Page.Locator("#Role option").AllAsync();
        Assert.True(options.Count >= 3);
        var optionTexts2 = await Task.WhenAll(options.Select(async element => await element.TextContentAsync()));
        Assert.DoesNotContain(optionTexts2, text => text.Equals("store role", StringComparison.InvariantCultureIgnoreCase));

        await s.Page.Locator("#Email").FillAsync(s.AsTestAccount().Email);
        await s.Page.Locator("#Role").SelectOptionAsync("Owner");
        await s.Page.ClickAsync("#AddUser");
        Assert.Contains("The user already has the role Owner.", await s.Page.Locator(".validation-summary-errors").TextContentAsync());
        await s.Page.Locator("#Role").SelectOptionAsync("Manager");
        await s.Page.ClickAsync("#AddUser");
        Assert.Contains("The user is the last owner. Their role cannot be changed.", await s.Page.Locator(".validation-summary-errors").TextContentAsync());

        await s.GoToStore(StoreNavPages.Roles);
        await s.ClickPagePrimary();
        await s.Page.Locator("#Role").FillAsync("Malice");

        await s.Page.EvaluateAsync(
            $"document.getElementById('Policies')['{Policies.CanModifyServerSettings}']=new Option('{Policies.CanModifyServerSettings}', '{Policies.CanModifyServerSettings}', true,true);");

        await s.ClickPagePrimary();
        await s.FindAlertMessage();
        Assert.Contains("Malice", await s.Page.ContentAsync());
        Assert.DoesNotContain(Policies.CanModifyServerSettings, await s.Page.ContentAsync());
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanUseWalletRoles()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        await s.StartAsync();

        var admin = await s.RegisterNewUser(true);
        await s.SkipWizard();
        var (_, storeId) = await s.CreateNewStore();
        await s.GoToStore();
        await using var scope = s.Server.PayTester.GetService<IServiceScopeFactory>().CreateAsyncScope();
        var storeRepo = scope.ServiceProvider.GetRequiredService<StoreRepository>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var handlers = scope.ServiceProvider.GetRequiredService<PaymentMethodHandlerDictionary>();
        var store = await storeRepo.FindStore(storeId);
        Assert.NotNull(store);
        var network = s.Server.NetworkProvider.GetNetwork<BTCPayNetwork>("BTC");
        var derivation = DerivationSchemeSettings.Parse(
            "tpubD6NzVbkrYhZ4XxNXjYTcRujMc8z8734diCthtFGgDMimbG5hUsKBuSTCuUyxWL7YwP7R4A5StMTRQiZnb6vE4pdHWPgy9hbiHuVJfBMumUu-[legacy]",
            network);
        store.SetPaymentMethodConfig(handlers.GetBitcoinHandler("BTC"), derivation);
        await storeRepo.UpdateStore(store);
        var walletId = new WalletId(storeId, "BTC");
        s.WalletId = walletId;
        var walletIdString = walletId.ToString();
        var cryptoCode = walletId.CryptoCode;

        await s.Logout();
        await s.GoToRegister();
        var walletManager = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        var multisigner = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        var multisignerGuest = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();

        var walletManagerUser = await userManager.FindByEmailAsync(walletManager);
        var multisignerUser = await userManager.FindByEmailAsync(multisigner);
        var multisignerGuestUser = await userManager.FindByEmailAsync(multisignerGuest);
        Assert.NotNull(walletManagerUser);
        Assert.NotNull(multisignerUser);
        Assert.NotNull(multisignerGuestUser);
        await storeRepo.AddOrUpdateStoreUser(storeId, walletManagerUser.Id, new StoreRoleId("Wallet Manager"));
        await storeRepo.AddOrUpdateStoreUser(storeId, multisignerUser.Id, new StoreRoleId("Multisigner"));
        await storeRepo.AddOrUpdateStoreUser(storeId, multisignerGuestUser.Id, new StoreRoleId("Multisigner Guest"));
        var walletManagerStore = await storeRepo.FindStore(storeId, walletManagerUser.Id);
        Assert.NotNull(walletManagerStore);
        Assert.True(walletManagerStore.HasPermission(walletManagerUser.Id, Policies.CanManageWallets));
        Assert.True(walletManagerStore.HasPermission(walletManagerUser.Id, Policies.CanManageWalletSettings));
        Assert.True(walletManagerStore.HasPermission(walletManagerUser.Id, Policies.CanModifyBitcoinOnchain));

        string StoreIndex(string id) => $"/stores/{id}/index";
        string StorePath(string id, string subPath) => $"/stores/{id}/{subPath}";
        string WalletsIndex() => "/wallets";
        string WalletTx(string id) => $"/wallets/{id}";
        string WalletSend(string id) => $"/wallets/{id}/send";
        string WalletPsbt(string id) => $"/wallets/{id}/psbt";
        string WalletSettings(string id, string code) => $"/stores/{id}/onchain/{code}/settings";

        await s.LogIn(walletManager);
        await s.AssertPageAccess(true, StoreIndex(storeId));
        await s.AssertPageAccess(true, WalletsIndex());
        await s.AssertPageAccess(true, WalletTx(walletIdString));
        await s.AssertPageAccess(true, WalletSend(walletIdString));
        await s.AssertPageAccess(true, WalletPsbt(walletIdString));
        await s.AssertPageAccess(true, WalletSettings(storeId, cryptoCode));
        await s.AssertPageAccess(false, StorePath(storeId, "invoices"));
        await s.AssertPageAccess(false, StorePath(storeId, "reports"));
        await s.AssertPageAccess(false, StorePath(storeId, "payment-requests"));
        await s.AssertPageAccess(false, StorePath(storeId, "pull-payments"));
        await s.AssertPageAccess(false, StorePath(storeId, "payouts"));
        await s.Logout();

        await s.LogIn(multisigner);
        await s.AssertPageAccess(true, StoreIndex(storeId));
        await s.AssertPageAccess(true, WalletsIndex());
        await s.AssertPageAccess(true, WalletTx(walletIdString));
        await s.AssertPageAccess(true, WalletSend(walletIdString));
        await s.AssertPageAccess(true, WalletPsbt(walletIdString));
        await s.AssertPageAccess(false, WalletSettings(storeId, cryptoCode));
        await s.AssertPageAccess(false, StorePath(storeId, "invoices"));
        await s.AssertPageAccess(false, StorePath(storeId, "reports"));
        await s.AssertPageAccess(false, StorePath(storeId, "payment-requests"));
        await s.AssertPageAccess(false, StorePath(storeId, "pull-payments"));
        await s.AssertPageAccess(false, StorePath(storeId, "payouts"));
        await s.Logout();

        await s.LogIn(multisignerGuest);
        await s.AssertPageAccess(true, StoreIndex(storeId));
        await s.AssertPageAccess(true, WalletsIndex());
        await s.AssertPageAccess(true, WalletTx(walletIdString));
        await s.AssertPageAccess(false, WalletSend(walletIdString));
        await s.AssertPageAccess(true, WalletPsbt(walletIdString));
        await s.AssertPageAccess(false, WalletSettings(storeId, cryptoCode));
        await s.AssertPageAccess(false, StorePath(storeId, "invoices"));
        await s.AssertPageAccess(false, StorePath(storeId, "reports"));
        await s.AssertPageAccess(false, StorePath(storeId, "payment-requests"));
        await s.AssertPageAccess(false, StorePath(storeId, "pull-payments"));
        await s.AssertPageAccess(false, StorePath(storeId, "payouts"));
        await s.Logout();
        await s.AssertPageDeniedOrLogin(StorePath(storeId, $"onchain/{cryptoCode}"));
        await s.AssertPageDeniedOrLogin(WalletSettings(storeId, cryptoCode));
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanCollectMultisigSignerKeysFromInvitedUsers()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        await s.StartAsync();

        var owner = await s.RegisterNewUser(true);
        await s.SkipWizard();
        var (_, storeId) = await s.CreateNewStore();
        await s.Logout();
        await s.GoToRegister();
        var signerA = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        var signerB = await s.RegisterNewUser();
        await s.SkipWizard();

        await using var scope = s.Server.PayTester.GetService<IServiceScopeFactory>().CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var storeRepo = scope.ServiceProvider.GetRequiredService<StoreRepository>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("MultisigInviteLink");

        var signerAUser = await userManager.FindByEmailAsync(signerA);
        var signerBUser = await userManager.FindByEmailAsync(signerB);
        Assert.NotNull(signerAUser);
        Assert.NotNull(signerBUser);

        await storeRepo.AddOrUpdateStoreUser(storeId, signerAUser.Id, new StoreRoleId("Multisigner Guest"));
        await storeRepo.AddOrUpdateStoreUser(storeId, signerBUser.Id, new StoreRoleId("Multisigner Guest"));

        await s.Logout();
        await s.GoToLogin();
        await s.LogIn(owner);
        await s.GoToUrl($"/stores/{storeId}/onchain/BTC/import/multisig");

        await s.Page.Locator($"label.form-check:has-text('{signerA}') input[type='checkbox']").CheckAsync();
        await s.Page.Locator($"label.form-check:has-text('{signerB}') input[type='checkbox']").CheckAsync();
        await s.Page.FillAsync("#MultisigRequiredSigners", "2");
        await s.Page.SelectOptionAsync("#MultisigScriptType", "p2wsh");
        await s.Page.ClickAsync("button[value='create-request']");
        await s.FindAlertMessage(partialText: "Multisig signer requests were created.");

        var pending = await storeRepo.GetSettingAsync<PendingMultisigSetupData>(storeId, "PendingMultisigSetup-BTC");
        Assert.NotNull(pending);
        Assert.Equal(2, pending.Participants.Count);
        Assert.All(pending.Participants, p => Assert.True(string.IsNullOrEmpty(p.AccountKey)));

        async Task SubmitSigner(string email, string userId, string accountKey, string fingerprint)
        {
            await s.Logout();
            await s.GoToLogin();
            await s.LogIn(email);
            var expires = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();
            var token = protector.Protect($"{storeId}|BTC|{pending.RequestId}|{userId}|{expires}");
            await s.GoToUrl($"/stores/{storeId}/onchain/BTC/multisig/invite/{Uri.EscapeDataString(token)}");
            await s.Page.FillAsync("#AccountKey", accountKey);
            await s.Page.FillAsync("#MasterFingerprint", fingerprint);
            await s.Page.FillAsync("#AccountKeyPath", "m/84'/1'/0'");
            await s.Page.ClickAsync("button[type='submit']");
            await Expect(s.Page.Locator("body")).ToContainTextAsync("already submitted");
        }

        await SubmitSigner(signerA, signerAUser.Id, "tpubDCgmrx4qnkbYuWijSfmBs5AUCicVfzh2ybq6hwiXMSKGawyHGr2tZfREkRphabUqktgTb3rbLBQzCUtQbRuYcpwPT2CFhps11116QLqkvGn", "e4a83d0b");
        await SubmitSigner(signerB, signerBUser.Id, "tpubDCU8fn9KJZavp82kBVaGR6TU6kpQtDDbP3YB4NEyfgidq5P7F1eU3BZnwsjFwSyxRCP1JWbSbshhRMGvrDKzMWHLZS2nzZPszSeL67R1oV5", "01a1e638");

        await s.Logout();
        await s.GoToLogin();
        await s.LogIn(owner);
        await s.GoToUrl($"/stores/{storeId}/onchain/BTC/import/multisig?MultisigRequestId={pending.RequestId}");
        await Expect(s.Page.Locator("button[value='finalize-request']")).ToBeEnabledAsync();
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task ServerRolesLinkedCorrectlyFromStoreRolesPage()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        await s.StartAsync();
        await s.RegisterNewUser(true);
        var (_, storeId) = await s.CreateNewStore();

        // Navigate to store roles page
        await s.GoToStore(StoreNavPages.Roles);

        // Create a custom store role first
        await s.ClickPagePrimary(); // Click "Add Role" button

        var customRoleName = "CustomStoreRole";
        await s.Page.FillAsync("input[name='Role']", customRoleName);

        // Select some permissions for the custom role
        await s.Page.Locator("input.policy-cb").First.CheckAsync();

        // Save the custom role
        await s.ClickPagePrimary();
        await s.FindAlertMessage(partialText: "Role created");

        // Test 1: Verify custom store role links to store roles URL
        var customRoleRow = s.Page.Locator($"tr:has-text('{customRoleName}')");

        // Verify it has "Store-level" badge
        await Expect(customRoleRow.Locator("span.badge.bg-light:has-text('Store-level')")).ToBeVisibleAsync();

        // Click Edit on custom store role
        await customRoleRow.Locator("a:has-text('Edit')").ClickAsync();

        var customRoleUrl = s.Page.Url;
        Assert.Contains($"/stores/{storeId}/roles/{customRoleName}", customRoleUrl);
        Assert.DoesNotContain("/server/roles/", customRoleUrl);

        // Go back to roles list
        await s.GoToStore(StoreNavPages.Roles);

        // Test 2: Verify server-wide role links to server roles URL
        // Click the Edit link for the first server-wide role
        await s.Page.Locator("tr:has(span.badge.bg-dark:has-text('Server-wide'))").First.Locator("a:has-text('Edit')").ClickAsync();

        // Verify we're redirected to server roles page, not store roles page
        var serverRoleUrl = s.Page.Url;

        // URL should be /server/roles/{roleName}, not /stores/{storeId}/roles/{roleName}
        Assert.Contains("/server/roles/", serverRoleUrl);
        Assert.DoesNotContain($"/stores/{storeId}/roles/", serverRoleUrl);

        // Verify we can see the permissions form
        await Expect(s.Page.Locator("form")).ToBeVisibleAsync();

        // Verify we have permission checkboxes
        await Expect(s.Page.Locator("input.policy-cb")).Not.ToHaveCountAsync(0);
    }

    [Fact]
    [Trait("Lightning", "Lightning")]
    [Trait("Playwright", "Playwright")]
    public async Task CanUsePredefinedRoles()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        s.Server.ActivateLightning();
        await s.StartAsync();
        await s.Server.EnsureChannelsSetup();
        var storeSettingsPaths = new[]
        {
            "settings", "rates", "checkout", "tokens", "users", "roles", "webhooks", "payout-processors",
            "payout-processors/onchain-automated/BTC", "payout-processors/lightning-automated/BTC", "emails/rules", "email-settings", "forms"
        };

        // Setup users
        var manager = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        var employee = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        var guest = await s.RegisterNewUser();
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();

        // Setup store, wallets and add users
        await s.RegisterNewUser(true);
        var (_, storeId) = await s.CreateNewStore();
        await s.GoToStore();
        await s.GenerateWallet(isHotWallet: true);
        await s.AddLightningNode(LightningConnectionType.CLightning, false);
        await s.AddUserToStore(storeId, manager, "Manager");
        await s.AddUserToStore(storeId, employee, "Employee");
        await s.AddUserToStore(storeId, guest, "Guest");

        // Add apps
        var (_, posId) = await s.CreateApp("PointOfSale");
        var (_, crowdfundId) = await s.CreateApp("Crowdfund");

        string GetStorePath(string subPath) => $"/stores/{storeId}" + (string.IsNullOrEmpty(subPath) ? "" : $"/{subPath}");

        // Owner access
        await s.AssertPageAccess(true, GetStorePath(""));
        await s.AssertPageAccess(true, GetStorePath("reports"));
        await s.AssertPageAccess(true, GetStorePath("invoices"));
        await s.AssertPageAccess(true, GetStorePath("invoices/create"));
        await s.AssertPageAccess(true, GetStorePath("payment-requests"));
        await s.AssertPageAccess(true, GetStorePath("payment-requests/edit"));
        await s.AssertPageAccess(true, GetStorePath("pull-payments"));
        await s.AssertPageAccess(true, GetStorePath("payouts"));
        await s.AssertPageAccess(true, GetStorePath("onchain/BTC"));
        await s.AssertPageAccess(true, GetStorePath("onchain/BTC/settings"));
        await s.AssertPageAccess(true, GetStorePath("lightning/BTC"));
        await s.AssertPageAccess(true, GetStorePath("lightning/BTC/settings"));
        await s.AssertPageAccess(true, GetStorePath("apps/create"));
        await s.AssertPageAccess(true, $"/apps/{posId}/settings/pos");
        await s.AssertPageAccess(true, $"/apps/{crowdfundId}/settings/crowdfund");
        foreach (var path in storeSettingsPaths)
        {
            // should have manage access to settings, hence should see submit buttons or create links
            s.TestLogs.LogInformation($"Checking access to store page {path} as owner");
            await s.AssertPageAccess(true, $"/stores/{storeId}/{path}");
            await s.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            if (path != "payout-processors")
            {
                var saveButton = s.Page.GetByRole(AriaRole.Button, new() { Name = "Save" });
                if (await saveButton.CountAsync() > 0)
                {
                    Assert.True(await saveButton.IsVisibleAsync());
                }
            }
        }

        await s.Logout();

        // Manager access
        await s.LogIn(manager);
        await s.AssertPageAccess(false, GetStorePath(""));
        await s.AssertPageAccess(true, GetStorePath("reports"));
        await s.AssertPageAccess(true, GetStorePath("invoices"));
        await s.AssertPageAccess(true, GetStorePath("invoices/create"));
        await s.AssertPageAccess(true, GetStorePath("payment-requests"));
        await s.AssertPageAccess(true, GetStorePath("payment-requests/edit"));
        await s.AssertPageAccess(true, GetStorePath("pull-payments"));
        await s.AssertPageAccess(true, GetStorePath("payouts"));
        await s.AssertPageAccess(false, GetStorePath("onchain/BTC"));
        await s.AssertPageAccess(false, GetStorePath("onchain/BTC/settings"));
        await s.AssertPageAccess(false, GetStorePath("lightning/BTC"));
        await s.AssertPageAccess(false, GetStorePath("lightning/BTC/settings"));
        await s.AssertPageAccess(false, GetStorePath("apps/create"));
        await s.AssertPageAccess(true, $"/apps/{posId}/settings/pos");
        await s.AssertPageAccess(true, $"/apps/{crowdfundId}/settings/crowdfund");
        foreach (var path in storeSettingsPaths)
        {
            // should have view access to settings, but no submit buttons or create links
            s.TestLogs.LogInformation($"Checking access to store page {path} as manager");
            await s.AssertPageAccess(true, $"stores/{storeId}/{path}");
            Assert.False(await s.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).IsVisibleAsync());
        }

        await s.Logout();

        // Employee access
        await s.LogIn(employee);
        await s.AssertPageAccess(false, GetStorePath(""));
        await s.AssertPageAccess(false, GetStorePath("reports"));
        await s.AssertPageAccess(true, GetStorePath("invoices"));
        await s.AssertPageAccess(true, GetStorePath("invoices/create"));
        await s.AssertPageAccess(true, GetStorePath("payment-requests"));
        await s.AssertPageAccess(true, GetStorePath("payment-requests/edit"));
        await s.AssertPageAccess(true, GetStorePath("pull-payments"));
        await s.AssertPageAccess(true, GetStorePath("payouts"));
        await s.AssertPageAccess(false, GetStorePath("onchain/BTC"));
        await s.AssertPageAccess(false, GetStorePath("onchain/BTC/settings"));
        await s.AssertPageAccess(false, GetStorePath("lightning/BTC"));
        await s.AssertPageAccess(false, GetStorePath("lightning/BTC/settings"));
        await s.AssertPageAccess(false, GetStorePath("apps/create"));
        await s.AssertPageAccess(false, $"/apps/{posId}/settings/pos");
        await s.AssertPageAccess(false, $"/apps/{crowdfundId}/settings/crowdfund");
        foreach (var path in storeSettingsPaths)
        {
            // should not have access to settings
            s.TestLogs.LogInformation($"Checking access to store page {path} as employee");
            await s.AssertPageAccess(false, $"/stores/{storeId}/{path}");
        }

        await s.GoToHome();
        await s.Logout();

        // Guest access
        await s.LogIn(guest);
        await s.AssertPageAccess(false, GetStorePath(""));
        await s.AssertPageAccess(false, GetStorePath("reports"));
        await s.AssertPageAccess(true, GetStorePath("invoices"));
        await s.AssertPageAccess(true, GetStorePath("invoices/create"));
        await s.AssertPageAccess(true, GetStorePath("payment-requests"));
        await s.AssertPageAccess(false, GetStorePath("payment-requests/edit"));
        await s.AssertPageAccess(true, GetStorePath("pull-payments"));
        await s.AssertPageAccess(true, GetStorePath("payouts"));
        await s.AssertPageAccess(false, GetStorePath("onchain/BTC"));
        await s.AssertPageAccess(false, GetStorePath("onchain/BTC/settings"));
        await s.AssertPageAccess(false, GetStorePath("lightning/BTC"));
        await s.AssertPageAccess(false, GetStorePath("lightning/BTC/settings"));
        await s.AssertPageAccess(false, GetStorePath("apps/create"));
        await s.AssertPageAccess(false, $"/apps/{posId}/settings/pos");
        await s.AssertPageAccess(false, $"/apps/{crowdfundId}/settings/crowdfund");
        foreach (var path in storeSettingsPaths)
        {
            // should not have access to settings
            s.TestLogs.LogInformation($"Checking access to store page {path} as guest");
            await s.AssertPageAccess(false, $"/stores/{storeId}/{path}");
        }

        await s.GoToHome();
        await s.Logout();
    }

    [Fact]
    [Trait("Fast", "Fast")]
    public void CanParseStoreRoleId()
    {
        var id = StoreRoleId.Parse("test::lol");
        Assert.Equal("test", id.StoreId);
        Assert.Equal("lol", id.Role);
        Assert.Equal("test::lol", id.ToString());
        Assert.Equal("test::lol", id.Id);
        Assert.False(id.IsServerRole);

        id = StoreRoleId.Parse("lol");
        Assert.Null(id.StoreId);
        Assert.Equal("lol", id.Role);
        Assert.Equal("lol", id.ToString());
        Assert.Equal("lol", id.Id);
        Assert.True(id.IsServerRole);
    }
}
