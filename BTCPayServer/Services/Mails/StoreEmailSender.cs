using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Stores;
using BTCPayServer.Data;

namespace BTCPayServer.Services.Mails
{
    class StoreEmailSender : EmailSender
    {
        public StoreEmailSender(StoreRepository storeRepository,
                                EmailSender fallback,
                                IBackgroundJobClient backgroundJobClient,
                                string storeId) : base(backgroundJobClient)
        {
            if (storeId == null)
                throw new ArgumentNullException(nameof(storeId));
            StoreRepository = storeRepository;
            FallbackSender = fallback;
            StoreId = storeId;
        }

        public StoreRepository StoreRepository { get; }
        public EmailSender FallbackSender { get; }
        public string StoreId { get; }

        public override async Task<EmailSettings> GetEmailSettings()
        {
            var store = await StoreRepository.FindStore(StoreId);
            var emailSettings = store.GetStoreBlob().EmailSettings;
            if (emailSettings?.IsComplete() == true)
            {
                return emailSettings;
            }
            return await FallbackSender.GetEmailSettings();
        }
    }
}
