#nullable enable
using System.Threading.Tasks;
using Xunit;

namespace BTCPayServer.Tests.PMO;

public class InvoiceCheckoutPMO(PlaywrightTester s)
{
    public class InvoiceAssertions
    {
        public string? AmountDue { get; set; }
        public string? TotalFiat { get; set; }
    }

    public async Task AssertContent(InvoiceAssertions assertions)
    {
        if (assertions.AmountDue is not null)
        {
            var el = await s.Page.WaitForSelectorAsync("#AmountDue");
            var content = await el!.TextContentAsync();
            Assert.Equal(assertions.AmountDue.NormalizeWhitespaces(), content.NormalizeWhitespaces());
        }

        if (assertions.TotalFiat is not null)
        {
            await s.Page.ClickAsync("#DetailsToggle");
            var el = await s.Page.WaitForSelectorAsync("#total_fiat");
            var content = await el!.TextContentAsync();
            Assert.Equal(assertions.TotalFiat.NormalizeWhitespaces(), content.NormalizeWhitespaces());
        }
    }

    public async Task ClickRedirect()
    => await s.Page.ClickAsync("#StoreLink");
}
