#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Plugins.Emails.Services;

class StoreEmailSender(
    StoreRepository storeRepository,
    EmailSender? fallback,
    IBackgroundJobClient backgroundJobClient,
    EventAggregator eventAggregator,
    string storeId,
    Logs logs)
    : EmailSender(backgroundJobClient, eventAggregator, logs)
{
    public StoreRepository StoreRepository { get; } = storeRepository;
    public EmailSender? FallbackSender { get; } = fallback;
    public string StoreId { get; } = storeId ?? throw new ArgumentNullException(nameof(storeId));

    public override async Task<EmailSettings?> GetEmailSettings()
    {
        var store = await StoreRepository.FindStore(StoreId);
        if (store is null)
            return null;
        var emailSettings = GetCustomSettings(store);
        if (emailSettings is not null)
            return emailSettings;
        if (FallbackSender is not null)
            return await FallbackSender.GetEmailSettings();
        return null;
    }
    public async Task<EmailSettings?> GetCustomSettings()
    {
        var store = await StoreRepository.FindStore(StoreId);
        if (store is null)
            return null;
        return GetCustomSettings(store);
    }
    EmailSettings? GetCustomSettings(StoreData store)
    {
        var emailSettings = store.GetStoreBlob().EmailSettings;
        return emailSettings?.IsComplete() is true ? emailSettings : null;
    }
}
