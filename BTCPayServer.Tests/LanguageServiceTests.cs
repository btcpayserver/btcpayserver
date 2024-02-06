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

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanTranslateLoginPage()
        {
            using var tester = CreateSeleniumTester(newDb: true);
            tester.Server.ActivateLangs();
            await tester.StartAsync();
            await tester.Server.PayTester.RestartStartupTask<LoadTranslationsStartupTask>();
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUpdateTranslationsInDatabase()
        {
            using var tester = CreateServerTester(newDb: true);
            await tester.StartAsync();
            var factory = tester.PayTester.GetService<ApplicationDbContextFactory>();
            var db = factory.CreateContext().Database.GetDbConnection();

            TestLogs.LogInformation("French fallback to english");
            await db.ExecuteAsync("INSERT INTO lang_dictionaries VALUES ('English', NULL, NULL), ('French', 'English', NULL)");

            Task translations_update(string dictId, (string Sentence, string Translation)[] translations)
            {
                return LocalizerService.translations_update(db, dictId, translations.Select(c => KeyValuePair.Create(c.Sentence, c.Translation)));
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

            await translations_update("English",
                [
                    ("Hello", "Hello"),
                    ("Goodbye", "Goodbye"),
                    ("Good afternoon", "Good afternoon")
                ]);
            await translations_update("French",
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
            await translations_update("French",
                [
                    ("Hello", null)
                ]);
            await AssertTranslations("French",
                [("Hello", "Hello"),
                ("Good afternoon", "Bonne aprem"),
                ("Goodbye", "Goodbye"),
                ("lol", null)]);

            TestLogs.LogInformation("Can use fallback by setting same as fallback to a sentence");
            await translations_update("French",
                [
                    ("Good afternoon", "Good afternoon")
                ]);
            await AssertTranslations("French",
                [("Hello", "Hello"),
                ("Good afternoon", "Good afternoon"),
                ("Goodbye", "Goodbye"),
                ("lol", null)]);

            await translations_update("English", [("Hello", null as string)]);
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
