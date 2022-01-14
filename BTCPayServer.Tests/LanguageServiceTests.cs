using System.Threading.Tasks;
using BTCPayServer.Services;
using BTCPayServer.Tests.Logging;
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
