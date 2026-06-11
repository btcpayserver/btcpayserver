using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Translations;
using BTCPayServer.Services;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using static Microsoft.Playwright.Assertions;
using Xunit;

namespace BTCPayServer.Tests
{
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class LanguageServiceTests : UnitTestBase
    {
        private class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new(handler, false);
        }

        private class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = new();
            public Dictionary<string, int> Calls { get; } = new();

            public void Register(string url, Func<HttpResponseMessage> responseFactory)
            {
                _responses[url] = responseFactory;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var url = request.RequestUri!.ToString();
                Calls[url] = Calls.TryGetValue(url, out var existing) ? existing + 1 : 1;

                if (_responses.TryGetValue(url, out var responseFactory))
                    return Task.FromResult(responseFactory());

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("Not Found", Encoding.UTF8, "text/plain")
                });
            }
        }

        public const int TestTimeout = TestUtils.TestTimeout;
        public LanguageServiceTests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Unit", "Unit")]
        public async Task LanguagePackUpdateService_ParsesManifestEntries()
        {
            var handler = new StubHttpMessageHandler();
            var manifestUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/manifest.json";
            handler.Register(manifestUrl, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "Languages": [
                        {
                          "Name": "French",
                          "Native": "Francais",
                          "File": "translations/french.json",
                          "Sha": "sha-fr",
                          "Maintainer": "alice|https://github.com/alice",
                          "Updated": "2026-05-01T10:00:00Z"
                        },
                        {
                          "Name": "German",
                          "Native": "Deutsch",
                          "File": "translations/german.json",
                          "Sha": "sha-de",
                          "Maintainer": null,
                          "Updated": "invalid"
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });

            var service = new LanguagePackUpdateService(new StubHttpClientFactory(handler), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
            var languages = await service.GetManifestLanguages();

            Assert.Equal(2, languages.Length);

            var french = languages.Single(l => l.Name == "French");
            Assert.Equal("Francais", french.Native);
            Assert.Equal("alice", french.MaintainerHandle);
            Assert.Equal("https://github.com/alice", french.MaintainerUrl);
            Assert.NotNull(french.Updated);
            Assert.Equal("translations/french.json", french.File);
            Assert.Equal("sha-fr", french.Sha);

            var german = languages.Single(l => l.Name == "German");
            Assert.Equal("Deutsch", german.Native);
            Assert.Null(german.MaintainerHandle);
            Assert.Null(german.MaintainerUrl);
            Assert.Null(german.Updated);
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Unit", "Unit")]
        public async Task LanguagePackUpdateService_PropagatesExceptionOnMalformedManifest()
        {
            var handler = new StubHttpMessageHandler();
            var manifestUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/manifest.json";
            handler.Register(manifestUrl, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ this is not: valid json", Encoding.UTF8, "application/json")
            });

            var service = new LanguagePackUpdateService(new StubHttpClientFactory(handler), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
            await Assert.ThrowsAnyAsync<Exception>(() => service.GetManifestLanguages());
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Unit", "Unit")]
        public async Task LanguagePackUpdateService_ThrowsOnMissingLanguagesKey()
        {
            var handler = new StubHttpMessageHandler();
            var manifestUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/manifest.json";
            handler.Register(manifestUrl, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"OtherField\": [] }", Encoding.UTF8, "application/json")
            });

            var service = new LanguagePackUpdateService(new StubHttpClientFactory(handler), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetManifestLanguages());
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Unit", "Unit")]
        public async Task LanguagePackUpdateService_ThrowsArgumentExceptionForUnknownLanguage()
        {
            var handler = new StubHttpMessageHandler();
            var manifestUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/manifest.json";
            handler.Register(manifestUrl, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{ "Languages": [ { "Name": "French", "File": "translations/french.json", "Sha": "deadbeef" } ] }""",
                    Encoding.UTF8, "application/json")
            });

            var service = new LanguagePackUpdateService(new StubHttpClientFactory(handler), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
            await Assert.ThrowsAsync<ArgumentException>(
                () => service.FetchLanguagePackFromRepository("Klingon"));
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Unit", "Unit")]
        public async Task LanguagePackUpdateService_ThrowsWhenManifestFails()
        {
            var handler = new StubHttpMessageHandler();
            var manifestUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/manifest.json";
            handler.Register(manifestUrl, () => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("error", Encoding.UTF8, "text/plain")
            });

            var service = new LanguagePackUpdateService(new StubHttpClientFactory(handler), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => service.GetManifestLanguages());
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Unit", "Unit")]
        public async Task LanguagePackUpdateService_FetchesLanguagePackFromManifest()
        {
            const string body = "{\"Hello\":\"Bonjour\"}";
            var expectedSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(body)));

            var handler = new StubHttpMessageHandler();
            var manifestUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/manifest.json";
            var translationUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/translations/french.json";

            handler.Register(manifestUrl, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""
                    {
                      "Languages": [
                        {
                          "Name": "French",
                          "File": "translations/french.json",
                          "Sha": "{{expectedSha}}"
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
            handler.Register(translationUrl, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

            var service = new LanguagePackUpdateService(new StubHttpClientFactory(handler), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
            var (translationsJson, version) = await service.FetchLanguagePackFromRepository("French");

            Assert.Equal(expectedSha, version, ignoreCase: true);
            Assert.Equal("Bonjour", JObject.Parse(translationsJson)["Hello"]?.ToString());
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Unit", "Unit")]
        public async Task LanguagePackUpdateService_RejectsLanguagePackOnShaMismatch()
        {
            const string body = "{\"Hello\":\"Bonjour\"}";

            var handler = new StubHttpMessageHandler();
            var manifestUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/manifest.json";
            var translationUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/translations/french.json";

            handler.Register(manifestUrl, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "Languages": [
                        {
                          "Name": "French",
                          "File": "translations/french.json",
                          "Sha": "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef"
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
            handler.Register(translationUrl, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

            var service = new LanguagePackUpdateService(new StubHttpClientFactory(handler), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.FetchLanguagePackFromRepository("French"));
            Assert.Contains("SHA-256 mismatch", ex.Message);
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Unit", "Unit")]
        public async Task LanguagePackUpdateService_UsesUpdateCacheUntilInvalidated()
        {
            var handler = new StubHttpMessageHandler();
            var manifestUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/manifest.json";

            handler.Register(manifestUrl, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "Languages": [
                        {
                          "Name": "French",
                          "File": "translations/french.json",
                          "Sha": "sha-1"
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });

            var service = new LanguagePackUpdateService(new StubHttpClientFactory(handler), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
            var outdatedMetadata = JObject.Parse("{ \"version\": \"sha-0\" }");
            var upToDateMetadata = JObject.Parse("{ \"version\": \"sha-1\" }");

            var first = await service.CheckForLanguagePackUpdateCached("French", outdatedMetadata);
            Assert.True(first);

            var cached = await service.CheckForLanguagePackUpdateCached("French", upToDateMetadata);
            Assert.True(cached);

            service.InvalidateCache("French");
            var refreshed = await service.CheckForLanguagePackUpdateCached("French", upToDateMetadata);
            Assert.False(refreshed);
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
            await tester.Page.Locator("#ConfirmInput").FillAsync("Delete");
            await tester.Page.Locator("#ConfirmContinue").ClickAsync();

            var alertMessage = await tester.FindAlertMessage();
            Assert.Contains("Translation English (Custom) deleted", await alertMessage.TextContentAsync());
            var pageContent = await tester.Page.ContentAsync();
            Assert.DoesNotContain("Select-English (Custom)", pageContent);
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Playwright", "Playwright")]
        public async Task LanguagePack_IsNotEditable_AndCanBeUninstalled_WithFallbackProtection()
        {
            await using var tester = CreatePlaywrightTester(newDb: true);
            await tester.StartAsync();
            await tester.RegisterNewUser(true);
            await tester.CreateNewStore();

            var factory = tester.Server.PayTester.GetService<ApplicationDbContextFactory>();
            var db = factory.CreateContext().Database.GetDbConnection();
            await db.ExecuteAsync("INSERT INTO lang_dictionaries VALUES ('French', 'English', 'LanguagePack')");
            await db.ExecuteAsync("INSERT INTO lang_dictionaries VALUES ('FrenchCustom', 'French', 'Custom')");
            await db.ExecuteAsync("INSERT INTO lang_dictionaries VALUES ('German', 'English', 'LanguagePack')");

            await tester.GoToServer(Views.Server.ServerNavPages.Translations);
            await tester.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            await Expect(tester.Page.Locator("#Delete-French")).ToBeVisibleAsync();
            await Expect(tester.Page.Locator("#Delete-German")).ToBeVisibleAsync();

            await Expect(tester.Page.Locator("a[href='/server/translations/French']")).ToHaveCountAsync(0);

            await tester.Page.Locator("#Delete-French").ClickAsync();
            await tester.Page.Locator("#ConfirmInput").FillAsync("Delete");
            await tester.Page.Locator("#ConfirmContinue").ClickAsync();

            var fallbackAlert = await tester.FindAlertMessage(StatusMessageModel.StatusSeverity.Error);
            Assert.Contains("Translation French cannot be uninstalled because it is used as fallback by: FrenchCustom", await fallbackAlert.TextContentAsync());

            await tester.Page.Locator("#Delete-German").ClickAsync();
            await tester.Page.Locator("#ConfirmInput").FillAsync("Delete");
            await tester.Page.Locator("#ConfirmContinue").ClickAsync();

            var successAlert = await tester.FindAlertMessage();
            Assert.Contains("Translation German deleted", await successAlert.TextContentAsync());

            var german = await db.QueryFirstOrDefaultAsync("SELECT 1 FROM lang_dictionaries WHERE dict_id='German'");
            Assert.Null(german);
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

            async Task SetTranslation(string translationId, (string Sentence, string Translation)[] translations)
            {
                var translation = await localizer.GetTranslation(translationId);
                Assert.NotNull(translation);
                var t = new Translations(translations.Select(t => KeyValuePair.Create(t.Sentence, t.Translation)));
                await localizer.Save(translation, t);
            }
            async Task AssertTranslations(string translationId, (string Sentence, string Expected)[] expectations)
            {
                var all = await db.QueryAsync<(string sentence, string translation)>("SELECT sentence, translation from translations WHERE dict_id=@dictId", new { dictId = translationId });
                foreach (var expectation in expectations)
                {
                    if (expectation.Expected is not null)
                        Assert.Equal(expectation.Expected, all.Single(a => a.sentence == expectation.Sentence).translation);
                    else
                        Assert.DoesNotContain(all, a => a.sentence == expectation.Sentence);
                }
            }

            await SetTranslation("English",
                [
                    ("Hello", "Hello"),
                    ("Goodbye", "Goodbye"),
                    ("Good afternoon", "Good afternoon")
                ]);
            await SetTranslation("French",
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
            await SetTranslation("French",
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
            await SetTranslation("French",
                [
                    ("Good afternoon", "Good afternoon")
                ]);
            await AssertTranslations("French",
                [("Hello", "Hello"),
                ("Good afternoon", "Good afternoon"),
                ("Goodbye", "Goodbye"),
                ("lol", null)]);

            await SetTranslation("English",
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
            await db.ExecuteAsync("DELETE FROM lang_dictionaries WHERE dict_id='French'");
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
