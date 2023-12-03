#nullable enable
using NBitcoin;
using System.Globalization;
using System.Linq;

namespace BTCPayServer.Services;

public class DefaultTransactionLinkProvider : TransactionLinkProvider
{
    public DefaultTransactionLinkProvider(string? blockExplorerLinkDefault)
    {
        BlockExplorerLinkDefault = blockExplorerLinkDefault;
    }

    public override string? OverrideBlockExplorerLink { get; set; }
    public override string? BlockExplorerLinkDefault { get; }

    public override string? GetTransactionLink(string paymentId)
    {
        if (string.IsNullOrEmpty(BlockExplorerLink))
            return null;
        paymentId = paymentId.Split('-').First();
        return string.Format(CultureInfo.InvariantCulture, BlockExplorerLink, paymentId);
    }
}
