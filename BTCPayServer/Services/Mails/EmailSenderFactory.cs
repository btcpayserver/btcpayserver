using BTCPayServer.HostedServices;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Services.Mails
{
    public class EmailSenderFactory
    {
        private readonly IBackgroundJobClient _jobClient;
        private readonly SettingsRepository _repository;
        private readonly StoreRepository _storeRepository;
        private readonly CssThemeManager _cssThemeManager;

        public EmailSenderFactory(IBackgroundJobClient jobClient,
            SettingsRepository repository,
            StoreRepository storeRepository,
            CssThemeManager cssThemeManager)
        {
            _jobClient = jobClient;
            _repository = repository;
            _storeRepository = storeRepository;
            _cssThemeManager = cssThemeManager;
        }

        public IEmailSender GetEmailSender(string storeId = null)
        {
            var serverSender = new ServerEmailSender(_repository, _jobClient);
            if (string.IsNullOrEmpty(storeId))
                return serverSender;
            return new StoreEmailSender(_storeRepository,
                !_cssThemeManager.Policies.DisableStoresToUseServerEmailSettings ? serverSender : null, _jobClient,
                storeId);
        }
    }
}
