#nullable enable
using System.Collections.Generic;
using System;
using BTCPayServer.Payments;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using System.Linq;

namespace BTCPayServer.Services;

public class TransactionLinkProviders : Dictionary<string, TransactionLinkProvider>
{
    public SettingsRepository SettingsRepository { get; }

    public record Entry(string CryptoCode, TransactionLinkProvider Provider);
    public TransactionLinkProviders(IEnumerable<Entry> entries, SettingsRepository settingsRepository)
    {
        foreach (var e in entries)
        {
            TryAdd(e.CryptoCode, e.Provider);
        }
        SettingsRepository = settingsRepository;
    }

    public async Task RefreshTransactionLinkTemplates()
    {
        var settings = await SettingsRepository.GetSettingAsync<PoliciesSettings>();
        if (settings?.BlockExplorerLinks is {} links)
        {
            foreach ((var pmi, var prov) in this)
            {
                var overrideLink = links.FirstOrDefault(item =>
                    item.CryptoCode.Equals(pmi, StringComparison.InvariantCultureIgnoreCase));
                prov.OverrideBlockExplorerLink = overrideLink?.Link ?? prov.BlockExplorerLinkDefault;
            }
        }
    }

    public string? GetTransactionLink(string cryptoCode, string paymentId)
    {
        ArgumentNullException.ThrowIfNull(cryptoCode);
        ArgumentNullException.ThrowIfNull(paymentId);
        TryGetValue(cryptoCode, out var p);
        return p?.GetTransactionLink(paymentId);
    }

    public string? GetBlockExplorerLink(string cryptoCode)
    {
        TryGetValue(cryptoCode, out var p);
        return p?.BlockExplorerLink;
    }
    public string? GetDefaultBlockExplorerLink(string cryptoCode)
    {
        TryGetValue(cryptoCode, out var p);
        return p?.BlockExplorerLinkDefault;
    }
}
