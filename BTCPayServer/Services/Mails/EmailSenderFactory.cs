#nullable enable
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Services.Mails
{
    public class EmailSenderFactory
    {
        public ISettingsAccessor<PoliciesSettings> PoliciesSettings { get; }
        public Logs Logs { get; }

        private readonly IBackgroundJobClient _jobClient;
        private readonly SettingsRepository _settingsRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly StoreRepository _storeRepository;

        public EmailSenderFactory(IBackgroundJobClient jobClient,
            SettingsRepository settingsSettingsRepository,
            EventAggregator eventAggregator,
            ISettingsAccessor<PoliciesSettings> policiesSettings,
            StoreRepository storeRepository,
            Logs logs)
        {
            Logs = logs;
            _jobClient = jobClient;
            _settingsRepository = settingsSettingsRepository;
            _eventAggregator = eventAggregator;
            PoliciesSettings = policiesSettings;
            _storeRepository = storeRepository;
        }

        public Task<IEmailSender> GetEmailSender(string? storeId = null)
        {
            var serverSender = new ServerEmailSender(_settingsRepository, _jobClient, _eventAggregator, Logs);
            if (string.IsNullOrEmpty(storeId))
                return Task.FromResult<IEmailSender>(serverSender);
            return Task.FromResult<IEmailSender>(new StoreEmailSender(_storeRepository,
                !PoliciesSettings.Settings.DisableStoresToUseServerEmailSettings ? serverSender : null, _jobClient,
                _eventAggregator, storeId, Logs));
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
}
