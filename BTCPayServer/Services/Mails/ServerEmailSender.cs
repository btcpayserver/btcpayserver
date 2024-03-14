using System;
using System.Threading.Tasks;
using BTCPayServer.Logging;

namespace BTCPayServer.Services.Mails
{
    class ServerEmailSender : EmailSender
    {
        public ServerEmailSender(SettingsRepository settingsRepository,
                                IBackgroundJobClient backgroundJobClient,
                                Logs logs) : base(backgroundJobClient, logs)
        {
            ArgumentNullException.ThrowIfNull(settingsRepository);
            SettingsRepository = settingsRepository;
        }

        public SettingsRepository SettingsRepository { get; }

        public override Task<EmailSettings> GetEmailSettings()
        {
            return SettingsRepository.GetSettingAsync<EmailSettings>();
        }

        public override async Task<string> GetPrefixedSubject(string subject)
        {
            var settings = await SettingsRepository.GetSettingAsync<ServerSettings>();
            var prefix = string.IsNullOrEmpty(settings?.ServerName) ? "BTCPay Server" : settings.ServerName;
            return $"{prefix}: {subject}";
        }
    }
}
