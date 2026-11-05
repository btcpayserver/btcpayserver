#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Services;

namespace BTCPayServer.Plugins.Emails.Services;

class ServerEmailSender(SettingsRepository settingsRepository,
    IBackgroundJobClient backgroundJobClient,
    EventAggregator eventAggregator,
    EmailLogRepository emailLogs,
    Logs logs) : EmailSender(backgroundJobClient, eventAggregator, emailLogs, logs)
{
    public override Task<EmailSettings?> GetEmailSettings()
        => settingsRepository.GetSettingAsync<EmailSettings>();
}
