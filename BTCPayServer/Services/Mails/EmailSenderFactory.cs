using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Services.Mails
{
    public class EmailSenderFactory
    {
        private readonly IBackgroundJobClient _jobClient;
        private readonly SettingsRepository _settingsRepository;
        private readonly StoreRepository _storeRepository;

        public EmailSenderFactory(IBackgroundJobClient jobClient,
            SettingsRepository settingsSettingsRepository,
            StoreRepository storeRepository)
        {
            _jobClient = jobClient;
            _settingsRepository = settingsSettingsRepository;
            _storeRepository = storeRepository;
        }

        public async Task<IEmailSender> GetEmailSender(string storeId = null)
        {
            var serverSender = new ServerEmailSender(_settingsRepository, _jobClient);
            if (string.IsNullOrEmpty(storeId))
                return serverSender;
            return new StoreEmailSender(_storeRepository,
                !(await _settingsRepository.GetPolicies()).DisableStoresToUseServerEmailSettings ? serverSender : null, _jobClient,
                storeId);
        }
    }
}
