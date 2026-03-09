using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Hosting;
using BTCPayServer.Plugins.Translations;
using BTCPayServer.Services;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class LanguageServiceTests : UnitTestBase
    {
        public const int TestTimeout = TestUtils.TestTimeout;
        public LanguageServiceTests(ITestOutputHelper helper) : base(helper)
        {
        }

        void ActivateLangs(ServerTester s)
        {
            TestLogs.LogInformation("Activating Langs...");
            var dir = TestUtils.GetTestDataFullPath("Langs");
            var langdir = Path.Combine(s.PayTester._Directory, "Langs");
            Directory.CreateDirectory(langdir);
            foreach (var file in Directory.GetFiles(dir))
                File.Copy(file, Path.Combine(langdir, Path.GetFileName(file)));
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Playwright", "Playwright")]
        public async Task CanTranslateLoginPage()
        {
            await using var tester = CreatePlaywrightTester(newDb: true);
            ActivateLangs(tester.Server);
            await tester.StartAsync();
            await tester.Server.PayTester.RestartStartupTask<LoadTranslationsStartupTask>();

            // Check if the Cypherpunk translation has been loaded from the file
            await tester.RegisterNewUser(true);
            await tester.CreateNewStore();
            await tester.GoToServer(Views.Server.ServerNavPages.Translations);
            await tester.Page.Locator("#Select-Cypherpunk").ClickAsync();
            await tester.Logout();

            await Expect(tester.Page.Locator("label[for=\"Password\"]")).ToContainTextAsync("Cyphercode");
            await Expect(tester.Page.GetByTestId("header")).ToContainTextAsync("Yo at BTCPay Server");

            // Create English (Custom)
            await tester.LogIn(tester.CreatedUser);
            await tester.GoToServer(Views.Server.ServerNavPages.Translations);
            await tester.ClickPagePrimary();
            await tester.Page.Locator("[name='Name']").FillAsync("English (Custom)");
            await tester.ClickPagePrimary();
            var translations = tester.Page.Locator("[name='Translations']");
            await translations.ClearAsync();
            await translations.FillAsync("{ \"Password\": \"Mot de passe\" }");
            await tester.ClickPagePrimary();

            // Check English (Custom) can be selected
            await tester.Page.Locator("#Select-English\\ \\(Custom\\)").ClickAsync();
            await tester.Logout();
            await Expect(tester.Page.Locator("label[for=\"Password\"]")).ToContainTextAsync("Mot de passe");

            // Check if we can remove English (Custom)
            await tester.LogIn(tester.CreatedUser);
            await tester.GoToServer(Views.Server.ServerNavPages.Translations);
            await tester.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            var text = await tester.Page.ContentAsync();
            Assert.Contains("Select-Cypherpunk", text);
            Assert.DoesNotContain("Select-English (Custom)", text);
            // Cypherpunk is loaded from file, can't edit
            Assert.DoesNotContain("Delete-Cypherpunk", text);
            // English (Custom) is selected, can't edit
            Assert.DoesNotContain("Delete-English (Custom)", text);
            await tester.Page.Locator("#Select-Cypherpunk").ClickAsync();
            await tester.Page.Locator("#Delete-English\\ \\(Custom\\)").ClickAsync();
            await tester.Page.Locator("#ConfirmInput").FillAsync("DELETE");
            await tester.Page.Locator("#ConfirmContinue").ClickAsync();

            var alertMessage = await tester.FindAlertMessage();
            Assert.Contains("Dictionary English (Custom) deleted", await alertMessage.TextContentAsync());
            var pageContent = await tester.Page.ContentAsync();
            Assert.DoesNotContain("Select-English (Custom)", pageContent);
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUpdateTranslationsInDatabase()
        {
            using var tester = CreateServerTester(newDb: true);
            await tester.StartAsync();
            var localizer = tester.PayTester.GetService<LocalizerService>();
            var factory = tester.PayTester.GetService<ApplicationDbContextFactory>();
            var db = factory.CreateContext().Database.GetDbConnection();

            TestLogs.LogInformation("French fallback to english");
            await db.ExecuteAsync("INSERT INTO lang_dictionaries VALUES ('French', 'English', NULL)");

            async Task SetDictionary(string dictId, (string Sentence, string Translation)[] translations)
            {
                var dict = await localizer.GetDictionary(dictId);
                var t = new Translations(translations.Select(t => KeyValuePair.Create(t.Sentence, t.Translation)));
                await localizer.Save(dict, t);
            }
            async Task AssertTranslations(string dictionary, (string Sentence, string Expected)[] expectations)
            {
                var all = await db.QueryAsync<(string sentence, string translation)>($"SELECT sentence, translation from translations WHERE dict_id='{dictionary}'");
                foreach (var expectation in expectations)
                {
                    if (expectation.Expected is not null)
                        Assert.Equal(expectation.Expected, all.Single(a => a.sentence == expectation.Sentence).translation);
                    else
                        Assert.DoesNotContain(all, a => a.sentence == expectation.Sentence);
                }
            }

            await SetDictionary("English",
                [
                    ("Hello", "Hello"),
                    ("Goodbye", "Goodbye"),
                    ("Good afternoon", "Good afternoon")
                ]);
            await SetDictionary("French",
                [
                    ("Hello", "Salut"),
                    ("Good afternoon", "Bonne aprem")
                ]);

            TestLogs.LogInformation("French should override Hello and Good afternoon, but not Goodbye");
            await AssertTranslations("French",
                [("Hello", "Salut"),
                ("Good afternoon", "Bonne aprem"),
                ("Goodbye", "Goodbye"),
                ("lol", null)]);
            await AssertTranslations("English",
                [("Hello", "Hello"),
                ("Good afternoon", "Good afternoon"),
                ("Goodbye", "Goodbye"),
                ("lol", null)]);

            TestLogs.LogInformation("Can use fallback by setting null to a sentence");
            await SetDictionary("French",
                [
                    ("Good afternoon", "Bonne aprem"),
                    ("Goodbye", "Goodbye"),
                    ("Hello", null)
                ]);
            await AssertTranslations("French",
                [("Hello", "Hello"),
                ("Good afternoon", "Bonne aprem"),
                ("Goodbye", "Goodbye"),
                ("lol", null)]);

            TestLogs.LogInformation("Can use fallback by setting same as fallback to a sentence");
            await SetDictionary("French",
                [
                    ("Good afternoon", "Good afternoon")
                ]);
            await AssertTranslations("French",
                [("Hello", "Hello"),
                ("Good afternoon", "Good afternoon"),
                ("Goodbye", "Goodbye"),
                ("lol", null)]);

            await SetDictionary("English",
                [
                    ("Hello", null as string),
                    ("Good afternoon", "Good afternoon"),
                    ("Goodbye", "Goodbye")
                ]);
            await AssertTranslations("French",
                [("Hello", null),
                ("Good afternoon", "Good afternoon"),
                ("Goodbye", "Goodbye"),
                ("lol", null)]);
            await db.ExecuteAsync("DELETE FROM lang_dictionaries WHERE dict_id='English'");
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanAutoDetectLanguage()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var languageService = tester.PayTester.GetService<LanguageService>();

            // Most common format. First option does not have a quality score. Others do in descending order.
            // Result should be nl-NL (because the default weight is 1 for nl)
            var lang1 = languageService.FindLanguageInAcceptLanguageHeader("nl,fr;q=0.7,en;q=0.5");
            Assert.NotNull(lang1);
            Assert.Equal("nl-NL", lang1?.Code);

            // Most common format. First option does not have a quality score. Others do in descending order. This time the first option includes a country.
            // Result should be nl-NL (because the default weight is 1 for nl-BE and it does not exist in BTCPay Server, but nl-NL does and applies too for language "nl")
            var lang2 = languageService.FindLanguageInAcceptLanguageHeader("nl-BE,fr;q=0.7,en;q=0.5");
            Assert.NotNull(lang2);
            Assert.Equal("nl-NL", lang2?.Code);

            // Unusual format, but still valid. All values have a quality score and not ordered.
            // Result should be fr-FR (because 0.7 is the highest quality score)
            var lang3 = languageService.FindLanguageInAcceptLanguageHeader("nl;q=0.1,fr;q=0.7,en;q=0.5");
            Assert.NotNull(lang3);
            Assert.Equal("fr-FR", lang3?.Code);

            // Unusual format, but still valid. Some language is given that we don't have and a wildcard for everything else.
            // Result should be NULL, because "xx" does not exist and * is a wildcard and has no meaning.
            var lang4 = languageService.FindLanguageInAcceptLanguageHeader("xx,*;q=0.5");
            Assert.Null(lang4);
        }
    }
}
