using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Translations;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests;

public partial class LanguageServiceTests
{
    [Theory(Timeout = TestTimeout)]
    [Trait("Playwright", "Playwright")]
    [InlineData("fr-FR", "fr-FR", "Development")]
    [InlineData(null, "en", "Development")]
    [InlineData("   ", "en", "Development")]
    [InlineData("not_a_tag", "not_a_tag", "Development")]
    [InlineData("fr-FR", "fr-FR", "Production")]
    public async Task CanInstallAndUpdateLanguagePacks(string manifestCode, string expectedCode, string hostEnvironment)
    {
        await using var tester = CreatePlaywrightTester(newDb: true);
        tester.Server.PayTester.HostEnvironment = hostEnvironment;
        await tester.StartAsync();
        await tester.RegisterNewUser(true);
        await tester.CreateNewStore();
        var services = tester.Server.PayTester;
        const string body = """{"Password":"Published password"}""";
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body)));
        using var handler = new TestHttpMessageHandler();
        handler.Register(ManifestUrl, () => TestHttpMessageHandler.JsonResponse(JsonConvert.SerializeObject(new
        {
            Languages = new[]
            {
                new { Name = "French", Code = manifestCode, File = "french.json", Sha = sha, Rtl = true },
                new { Name = "German", Code = manifestCode, File = "german.json", Sha = sha, Rtl = true }
            }
        })));
        handler.Register(ManifestUrl.Replace("manifest.json", "french.json"), () => TestHttpMessageHandler.JsonResponse(body));
        handler.Register(ManifestUrl.Replace("manifest.json", "german.json"), () => TestHttpMessageHandler.JsonResponse(body));
        services.GetService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(LanguagePackUpdateService.HttpClientName)
            .HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler);
        var localizer = services.GetService<LocalizerService>();
        await localizer.SaveLanguagePack("German", Translations.CreateFromJson("""{"Password":"Installed password"}"""),
            "old-sha", new("de", false));
        var login = await tester.Browser.NewPageAsync();

        await tester.GoToServer(Views.Server.ServerNavPages.Translations);
        foreach (var (name, action) in new[] { ("French", "Install"), ("German", "Update") })
        {
            var row = tester.Page.GetByRole(Microsoft.Playwright.AriaRole.Row).Filter(new()
            {
                Has = tester.Page.GetByText(name, new() { Exact = true })
            });
            await row.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = action, Exact = true }).ClickAsync();
            await Expect(row.Locator(".installed-languages__code")).ToHaveTextAsync(expectedCode);
            await Expect(row.Locator(".installed-languages__rtl")).ToHaveTextAsync("Yes");
            await row.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Select", Exact = true }).ClickAsync();
            await Expect(tester.Page.Locator("html")).ToHaveAttributeAsync("lang", expectedCode);
            await Expect(tester.Page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
            await tester.Page.ReloadAsync();
            await Expect(tester.Page.Locator("html")).ToHaveAttributeAsync("lang", expectedCode);
            await login.GotoAsync(new Uri(tester.ServerUri, "login").ToString());
            await Expect(login.GetByText("Published password", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(login.Locator("html")).ToHaveAttributeAsync("lang", expectedCode);
            await Expect(login.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
            await Expect(login.Locator("html[data-devenv]")).ToHaveCountAsync(hostEnvironment == "Development" ? 1 : 0);
            await Expect(login.Locator("html[asp-devenv]")).ToHaveCountAsync(0);
        }
    }

    [Fact(Timeout = TestTimeout)]
    [Trait("Playwright", "Playwright")]
    public async Task CanConfigureCustomLanguageMetadata()
    {
        await using var tester = CreatePlaywrightTester(newDb: true);
        await tester.StartAsync();
        await tester.RegisterNewUser(true);
        await tester.CreateNewStore();
        var localizer = tester.Server.PayTester.GetService<LocalizerService>();
        await localizer.CreateTranslation("Arabic", "English", "LanguagePack");
        await localizer.UpdateMetadata("Arabic", "sha", new("ar", true));
        await tester.GoToServer(Views.Server.ServerNavPages.Translations);
        Microsoft.Playwright.ILocator InstalledRow(string name) =>
            tester.Page.Locator(".installed-languages__row").Filter(new()
            {
                Has = tester.Page.GetByText(name, new() { Exact = true })
            });
        await Expect(InstalledRow("English").Locator(".installed-languages__code")).ToHaveTextAsync("en");
        await Expect(InstalledRow("English").Locator(".installed-languages__rtl")).ToHaveTextAsync("No");
        await Expect(InstalledRow("Arabic").Locator(".installed-languages__code")).ToHaveTextAsync("ar");
        await Expect(InstalledRow("Arabic").Locator(".installed-languages__rtl")).ToHaveTextAsync("Yes");
        await tester.Page.GotoAsync(new Uri(tester.ServerUri, "server/translations/create?fallback=Arabic").ToString());
        await tester.Page.Locator("[name='Name']").FillAsync("Clone");
        await Expect(tester.Page.Locator(".translation-metadata__code")).ToHaveValueAsync("");
        await Expect(tester.Page.Locator(".translation-metadata__direction")).ToHaveValueAsync("");
        await tester.ClickPagePrimary();
        await tester.Page.Locator("[name='Translations']").FillAsync("""{"Password":"Custom password"}""");
        await tester.ClickPagePrimary();
        await Expect(InstalledRow("Clone").Locator(".installed-languages__code")).ToHaveTextAsync("ar");
        await Expect(InstalledRow("Clone").Locator(".installed-languages__rtl")).ToHaveTextAsync("Yes");
        await tester.TakeScreenshot("installed-language-metadata.png");
        await InstalledRow("Clone").GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Select", Exact = true }).ClickAsync();
        await Expect(tester.Page.Locator("html")).ToHaveAttributeAsync("lang", "ar");
        await Expect(tester.Page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
        await tester.Page.GotoAsync(new Uri(tester.ServerUri, "server/translations/Clone").ToString());
        await tester.Page.Locator(".translation-metadata__code").FillAsync(" en_US ");
        await tester.Page.Locator(".translation-metadata__direction").SelectOptionAsync("false");
        await tester.ClickPagePrimary();
        await Expect(tester.Page.Locator("html")).ToHaveAttributeAsync("lang", "en_US");
        await Expect(tester.Page.Locator("html[dir]")).ToHaveCountAsync(0);
        await Expect(InstalledRow("Clone").Locator(".installed-languages__code")).ToHaveTextAsync("en_US");
        await Expect(InstalledRow("Clone").Locator(".installed-languages__rtl")).ToHaveTextAsync("No");
        await tester.Page.GotoAsync(new Uri(tester.ServerUri, "server/translations/Clone").ToString());
        await Expect(tester.Page.Locator(".translation-metadata__code")).ToHaveValueAsync("en_US");
        await Expect(tester.Page.Locator(".translation-metadata__direction")).ToHaveValueAsync("false");
        // Keep the JSON editor compact so the settings remain legible in the
        // review screenshot instead of capturing thousands of translation rows.
        await tester.Page.Locator(".translation-editor").EvaluateAsync("element => element.rows = 6");
        await tester.TakeScreenshot("translation-metadata.png");
        await tester.Page.Locator(".translation-metadata__code").FillAsync("");
        await tester.Page.Locator(".translation-metadata__direction").SelectOptionAsync("true");
        await tester.ClickPagePrimary();
        await Expect(tester.Page.Locator("html")).ToHaveAttributeAsync("lang", "ar");
        await Expect(tester.Page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
        await Expect(tester.Page.Locator("html[use-translations]")).ToHaveCountAsync(0);
        await tester.Page.GotoAsync(new Uri(tester.ServerUri, "docs").ToString());
        await Expect(tester.Page.Locator("html")).ToHaveAttributeAsync("lang", "en");
        await Expect(tester.Page.Locator("html")).ToHaveAttributeAsync("dir", "ltr");
    }
}
