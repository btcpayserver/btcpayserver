#nullable enable
using System.Collections.Generic;
using System;
using BTCPayServer.Payments;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using System.Linq;

namespace BTCPayServer.Services;

public class TransactionLinkProviders : Dictionary<PaymentMethodId, TransactionLinkProvider>
{
    public SettingsRepository SettingsRepository { get; }

    public record Entry(PaymentMethodId PaymentMethodId, TransactionLinkProvider Provider);
    public TransactionLinkProviders(IEnumerable<Entry> entries, SettingsRepository settingsRepository)
    {
        foreach (var e in entries)
        {
            Add(e.PaymentMethodId, e.Provider);
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
                    item.CryptoCode.Equals(pmi.CryptoCode, StringComparison.InvariantCultureIgnoreCase) ||
                    item.CryptoCode.Equals(pmi.ToString(), StringComparison.InvariantCultureIgnoreCase));
                prov.OverrideBlockExplorerLink = overrideLink?.Link ?? prov.BlockExplorerLinkDefault;
            }
        }
    }

    public string? GetTransactionLink(PaymentMethodId paymentMethodId, string paymentId)
    {
        ArgumentNullException.ThrowIfNull(paymentMethodId);
        ArgumentNullException.ThrowIfNull(paymentId);
        TryGetValue(paymentMethodId, out var p);
        return p?.GetTransactionLink(paymentId);
    }

    public string? GetBlockExplorerLink(PaymentMethodId paymentMethodId)
    {
        TryGetValue(paymentMethodId, out var p);
        return p?.BlockExplorerLink;
    }
    public string? GetDefaultBlockExplorerLink(PaymentMethodId paymentMethodId)
    {
        TryGetValue(paymentMethodId, out var p);
        return p?.BlockExplorerLinkDefault;
    }
}
