#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Services;

namespace BTCPayServer.Plugins.Emails.Services;

class ServerEmailSender(SettingsRepository settingsRepository,
    IBackgroundJobClient backgroundJobClient,
    EventAggregator eventAggregator,
    ApplicationDbContextFactory dbContextFactory,
    Logs logs) : EmailSender(backgroundJobClient, eventAggregator, dbContextFactory, logs)
{
    public override Task<EmailSettings?> GetEmailSettings()
        => settingsRepository.GetSettingAsync<EmailSettings>();
}
