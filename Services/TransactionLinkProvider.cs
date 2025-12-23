#nullable enable
using BTCPayServer;

namespace BTCPayServer.Services;
public abstract class TransactionLinkProvider
{
    public abstract string? OverrideBlockExplorerLink { get; set;  }
    public abstract string? BlockExplorerLinkDefault { get; }

    public string? BlockExplorerLink => OverrideBlockExplorerLink ?? BlockExplorerLinkDefault;
    public abstract string? GetTransactionLink(string paymentId);
}
