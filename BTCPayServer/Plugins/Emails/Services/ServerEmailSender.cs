#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services;

namespace BTCPayServer.Plugins.Emails.Services;

class ServerEmailSender(SettingsRepository settingsRepository,
    IBackgroundJobClient backgroundJobClient,
    EventAggregator eventAggregator,
    Logs logs) : EmailSender(backgroundJobClient, eventAggregator, logs)
{
    public override Task<EmailSettings?> GetEmailSettings()
        => settingsRepository.GetSettingAsync<EmailSettings>();
}
