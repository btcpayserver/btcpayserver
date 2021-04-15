using System.Threading.Tasks;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Services.Mails
{
    public class EmailSenderFactory
    {
        private readonly IBackgroundJobClient _jobClient;
        private readonly SettingsRepository _repository;
        private readonly StoreRepository _storeRepository;

        public EmailSenderFactory(IBackgroundJobClient jobClient,
            SettingsRepository repository,
            StoreRepository storeRepository)
        {
            _jobClient = jobClient;
            _repository = repository;
            _storeRepository = storeRepository;
        }

        public async Task<IEmailSender> GetEmailSender(string storeId = null)
        {
            var policies = (await _repository.GetSettingAsync<PoliciesSettings>()) ?? new PoliciesSettings();
            var serverSender = new ServerEmailSender(_repository, _jobClient);
            if (string.IsNullOrEmpty(storeId))
                return serverSender;
            return new StoreEmailSender(_storeRepository,
                !policies.DisableStoresToUseServerEmailSettings ? serverSender : null, _jobClient,
                storeId);
        }
    }
}
