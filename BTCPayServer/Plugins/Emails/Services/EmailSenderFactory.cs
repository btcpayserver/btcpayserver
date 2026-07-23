#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Plugins.Emails.Services;

public class EmailSenderFactory(
    IBackgroundJobClient jobClient,
    SettingsRepository settingsSettingsRepository,
    EventAggregator eventAggregator,
    ISettingsAccessor<PoliciesSettings> policiesSettings,
    ApplicationDbContextFactory dbContextFactory,
    StoreRepository storeRepository,
    Logs logs)
{
    public Logs Logs { get; } = logs;

    public Task<IEmailSender> GetEmailSender(string? storeId = null)
    {
        var serverSender = new ServerEmailSender(settingsSettingsRepository, jobClient, eventAggregator, dbContextFactory, Logs);
        if (string.IsNullOrEmpty(storeId))
            return Task.FromResult<IEmailSender>(serverSender);
        return Task.FromResult<IEmailSender>(new StoreEmailSender(storeRepository,
            !policiesSettings.Settings.DisableStoresToUseServerEmailSettings ? serverSender : null, jobClient,
            eventAggregator, dbContextFactory, storeId, Logs));
    }

    public async Task<bool> IsComplete(string? storeId = null)
    {
        var settings = await this.GetSettings(storeId);
        return settings?.IsComplete() is true;
    }
    public async Task<EmailSettings?> GetSettings(string? storeId = null)
    {
        var sender = await this.GetEmailSender(storeId);
        return await sender.GetEmailSettings();
    }
}
