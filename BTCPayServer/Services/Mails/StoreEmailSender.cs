#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Services.Mails
{
    class StoreEmailSender : EmailSender
    {
        public StoreEmailSender(StoreRepository storeRepository,
                                EmailSender? fallback,
                                IBackgroundJobClient backgroundJobClient,
                                string storeId,
                                Logs logs) : base(backgroundJobClient, logs)
        {
            StoreId = storeId ?? throw new ArgumentNullException(nameof(storeId));
            StoreRepository = storeRepository;
            FallbackSender = fallback;
        }

        public StoreRepository StoreRepository { get; }
        public EmailSender? FallbackSender { get; }
        public string StoreId { get; }

        public override async Task<EmailSettings?> GetEmailSettings()
        {
            var store = await StoreRepository.FindStore(StoreId);
			if (store is null)
				return null;
            var emailSettings = GetCustomSettings(store);
            if (emailSettings is not null)
                return emailSettings;
            if (FallbackSender is not null)
                return await FallbackSender.GetEmailSettings();
            return null;
        }
        public async Task<EmailSettings?> GetCustomSettings()
        {
            var store = await StoreRepository.FindStore(StoreId);
            if (store is null)
                return null;
            return GetCustomSettings(store);
        }
        EmailSettings? GetCustomSettings(StoreData store)
        {    
            var emailSettings = store.GetStoreBlob().EmailSettings;
            if (emailSettings?.IsComplete() is true)
            {
                return emailSettings;
            }
            return null;
        }

        public override async Task<string> GetPrefixedSubject(string subject)
        {
            var store = await StoreRepository.FindStore(StoreId);
            return string.IsNullOrEmpty(store?.StoreName) ? subject : $"{store.StoreName}: {subject}";
        }
    }
}
