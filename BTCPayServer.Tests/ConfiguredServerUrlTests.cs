using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Models.AccountViewModels;
using BTCPayServer.Plugins.Multisig.Services;
using BTCPayServer.Services;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Wallets;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class ConfiguredServerUrlTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    private const string ConfiguredUrl = "https://canonical.example/btcpay";

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task UsesConfiguredServerUrlForGeneratedLinks()
    {
        const string password = "Kitten0@";
        await using var s = CreatePlaywrightTester(newDb: true);
        await s.StartAsync();
        var owner = Utils.GenerateEmail();
        var accountController = s.Server.PayTester.GetController<UIAccountController>();
        accountController.Request.PathBase = "/btcpay";
        await accountController.Register(new RegisterViewModel
        {
            Email = owner,
            Password = password,
            ConfirmPassword = password
        });

        var settingsRepository = s.Server.PayTester.GetService<SettingsRepository>();
        var serverSettings = await settingsRepository.GetSettingAsync<ServerSettings>();
        Assert.Equal($"http://127.0.0.1:{s.Server.PayTester.Port}/btcpay", serverSettings?.BaseUrl);
        await s.GoToLogin();
        await s.LogIn(owner, password);

        var (_, storeId) = await s.CreateNewStore();
        await s.GoToServer("MonetizationPlugin");
        await s.ClickPagePrimary();
        await s.ConfirmModal();
        await s.FindAlertMessage(partialText: "Monetization activated");

        serverSettings = await settingsRepository.GetSettingAsync<ServerSettings>() ?? new ServerSettings();
        serverSettings.UpdateBaseUrl(RequestBaseUrl.FromUrl(ConfiguredUrl));
        await settingsRepository.UpdateSetting(serverSettings);
        await s.Logout();
        await s.GoToLogin();
        await s.LogIn(owner, password);
        Assert.Equal(ConfiguredUrl, (await settingsRepository.GetSettingAsync<ServerSettings>())?.BaseUrl);

        await s.GoToProfile(ManageNavPages.APIKeys);
        await s.ClickPagePrimary();
        await s.ClickPagePrimary();
        var apiKey = await (await s.FindAlertMessage()).Locator("code").TextContentAsync();
        var apiKeyRow = s.Page.Locator("tr").Filter(new() { HasText = apiKey });
        await apiKeyRow.Locator("button[data-qr]").ClickAsync();
        var qrData = await s.Page.Locator(".truncate-center.form-control-plaintext").GetAttributeAsync("data-text");
        var apiKeyPayload = JObject.Parse(qrData!);
        Assert.Equal(apiKey, apiKeyPayload.Value<string>("apiKey"));
        Assert.Equal(ConfiguredUrl, apiKeyPayload.Value<string>("host"));
        await s.Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();

        await s.GoToProfile(ManageNavPages.LoginCodes);
        var loginCodeUrl = await s.Page.Locator("#LoginCode .qr-code").GetAttributeAsync("alt");
        Assert.StartsWith($"{ConfiguredUrl}/login?", loginCodeUrl);

        var externalServices = s.Server.PayTester.GetService<IOptions<ExternalServicesOptions>>().Value.ExternalServices;
        externalServices.Add(new ExternalService
        {
            Type = ExternalServiceTypes.LNDRest,
            ServiceName = "lndrest",
            DisplayName = "LND (REST)",
            CryptoCode = "BTC",
            ConnectionString = new ExternalConnectionString(new Uri("https://127.0.0.1:8080"))
            {
                Macaroon = [1, 2, 3]
            }
        });
        await s.GoToUrl("/server/services/lndrest/BTC");
        await s.Page.GetByRole(AriaRole.Button, new() { Name = "Show QR Code" }).ClickAsync();
        var lndConfig = await s.Page.Locator("#qrCodeData").GetAttributeAsync("data-url");
        Assert.Matches($"^config={Regex.Escape(ConfiguredUrl)}/lnd-config/[0-9]+/lnd\\.config$", lndConfig!);

        await using (await s.SwitchPage())
        {
            var registered = await s.Server.WaitForEvent<UserEvent.Registered>(async () =>
            {
                await s.GoToUrl("/monetization/new-user");
                var planCheckoutUrl = await s.Page.Locator(".plan-checkout__qr-modal .qr-code").GetAttributeAsync("alt");
                Assert.StartsWith($"{ConfiguredUrl}/plan-checkout/", planCheckoutUrl);
                await s.Page.FillAsync(".plan-checkout__email", "canonical-user@example.com");
                await s.ClickPagePrimary();
            }, evt => evt.User.Email == "canonical-user@example.com");
            Assert.Equal(ConfiguredUrl, registered.RequestBaseUrl.ToString());
            Assert.StartsWith($"{ConfiguredUrl}/register/confirm-email?", registered.ConfirmationEmailLink);
        }

        await s.ConfigureServerEmailWithMailPit(
            from: "canonical-url@test.com",
            login: "canonical-url@test.com",
            password: "canonical-url@test.com");
        await s.GoToUrl($"/stores/{storeId}/onchain/BTC/import/multisig");
        await s.Page.Locator($"label.multisig-signer-item:has-text('{owner}') input[type='checkbox']").CheckAsync();
        await s.Page.FillAsync("#MultisigRequiredSigners", "1");
        await s.Page.FillAsync("#MultisigTotalSigners", "1");
        await s.Page.SelectOptionAsync("#MultisigScriptType", "p2wsh");
        var signerRequestEmail = await s.Server.AssertHasEmail(() => s.Page.ClickAsync("#CreateSignerRequest"));
        var multisigService = s.Server.PayTester.GetService<MultisigService>();
        var setup = Assert.Single(await multisigService.GetPendingMultisigSetup(storeId));
        Assert.Equal(ConfiguredUrl, setup.RequestBaseUrl.ToString());
        Assert.Contains($"{ConfiguredUrl}/multisig-setups/{setup.RequestId}",
            signerRequestEmail.Html ?? signerRequestEmail.Text ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        var rootKey = new Mnemonic("all all all all all all all all all all all all").DeriveExtKey();
        var accountKeyPath = KeyPath.Parse("48'/1'/0'/2'");
        var accountKey = rootKey.Derive(accountKeyPath).Neuter().ToString(Network.RegTest);
        var rootedAccountKeyPath = new RootedKeyPath(rootKey.GetPublicKey().GetHDFingerPrint(), accountKeyPath);
        await s.Page.ClickAsync("#SubmitSignerKeyCta");
        await s.Page.ClickAsync("#SubmitSignerKeyManual");
        await s.Page.FillAsync("#DisplayAccountKey", accountKey);
        await s.Page.FillAsync("#AccountKeyPath", rootedAccountKeyPath.ToString());
        await s.Server.AssertHasEmail(() => s.Page.ClickAsync("button[type='submit']"));
        await Expect(s.Page.Locator("#FinalizeMultisig")).ToBeVisibleAsync();
        await s.Page.ClickAsync("#FinalizeMultisig");
        await Expect(s.Page.Locator("#Confirm")).ToBeVisibleAsync();
        await s.Server.AssertHasEmail(() => s.Page.ClickAsync("#Confirm"));

        await s.GoToWallet(navPages: WalletsNavPages.Receive);
        var addressElement = s.Page.Locator("#Address");
        await addressElement.ClickAsync();
        var address = await addressElement.GetAttributeAsync("data-text");
        Assert.NotNull(address);
        await s.Page.ClickAsync("button[value='fill-wallet']");
        await s.Page.ClickAsync("#CancelWizard");
        await s.GoToWallet(navPages: WalletsNavPages.Send);
        await s.Page.FillAsync("#Outputs_0__DestinationAddress", address);
        await s.Page.FillAsync("#Outputs_0__Amount", "0.1");
        await s.Page.ClickAsync("#CreatePendingTransaction");
        var pendingTransactionService = s.Server.PayTester.GetService<PendingTransactionService>();
        var pending = Assert.Single(await pendingTransactionService.GetPendingTransactions("BTC", storeId));
        Assert.Equal(ConfiguredUrl, pending.GetBlob().RequestBaseUrl);
    }
}
