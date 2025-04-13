using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Hosting;
using BTCPayServer.Services;
using Dapper;
using Microsoft.EntityFrameworkCore;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.Extensions;
using Xunit;
using Xunit.Abstractions;
using static BTCPayServer.Services.LocalizerService;

namespace BTCPayServer.Tests
{
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class LanguageServiceTests : UnitTestBase
    {
        public const int TestTimeout = TestUtils.TestTimeout;
        public LanguageServiceTests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Selenium", "Selenium")]
        public async Task CanTranslateLoginPage()
        {
            using var tester = CreateSeleniumTester(newDb: true);
            tester.Server.ActivateLangs();
            await tester.StartAsync();
            await tester.Server.PayTester.RestartStartupTask<LoadTranslationsStartupTask>();

            // Check if the Cypherpunk translation has been loaded from the file
            tester.RegisterNewUser(true);
            tester.CreateNewStore();
            tester.GoToServer(Views.Server.ServerNavPages.Translations);
            tester.Driver.FindElement(By.Id("Select-Cypherpunk")).Click();
            tester.Logout();
            Assert.Contains("Cyphercode", tester.Driver.PageSource);
            Assert.Contains("Yo at BTCPay Server", tester.Driver.PageSource);

            // Create English (Custom) 
            tester.LogIn();
            tester.GoToServer(Views.Server.ServerNavPages.Translations);
            tester.ClickPagePrimary();
            tester.Driver.FindElement(By.Name("Name")).SendKeys("English (Custom)");
            tester.ClickPagePrimary();
            var translations = tester.Driver.FindElement(By.Name("Translations"));
            translations.Clear();
            translations.SendKeys("{ \"Password\": \"Mot de passe\" }");
            tester.ClickPagePrimary();

            // Check English (Custom) can be selected
            tester.Driver.FindElement(By.Id("Select-English (Custom)")).Click();
            tester.Logout();
            Assert.Contains("Mot de passe", tester.Driver.PageSource);

            // Check if we can remove English (Custom)
            tester.LogIn();
            tester.GoToServer(Views.Server.ServerNavPages.Translations);
            var text = tester.Driver.PageSource;
            Assert.Contains("Select-Cypherpunk", text);
            Assert.DoesNotContain("Select-English (Custom)", text);
            // Cypherpunk is loaded from file, can't edit
            Assert.DoesNotContain("Delete-Cypherpunk", text);
            // English (Custom) is selected, can't edit
            Assert.DoesNotContain("Delete-English (Custom)", text);
            tester.Driver.FindElement(By.Id("Select-Cypherpunk")).Click();
            tester.Driver.FindElement(By.Id("Delete-English (Custom)")).Click();
            tester.Driver.WaitForElement(By.Id("ConfirmInput")).SendKeys("DELETE");
            tester.Driver.FindElement(By.Id("ConfirmContinue")).Click();

            Assert.Contains("Dictionary English (Custom) deleted", tester.FindAlertMessage().Text);
            Assert.DoesNotContain("Select-English (Custom)", tester.Driver.PageSource);
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
