using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Mails
{
    class ServerEmailSender : EmailSender
    {
        public ServerEmailSender(SettingsRepository settingsRepository,
                                IBackgroundJobClient backgroundJobClient) : base(backgroundJobClient)
        {
            if (settingsRepository == null)
                throw new ArgumentNullException(nameof(settingsRepository));
            SettingsRepository = settingsRepository;
        }

        public SettingsRepository SettingsRepository { get; }

        public override Task<EmailSettings> GetEmailSettings()
        {
            return SettingsRepository.GetSettingAsync<EmailSettings>();
        }
    }
}
