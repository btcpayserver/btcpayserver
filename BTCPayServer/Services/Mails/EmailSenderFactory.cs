using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Services.Mails
{
    public class EmailSenderFactory
    {
        private readonly IBackgroundJobClient _JobClient;
        private readonly SettingsRepository _Repository;
        private readonly StoreRepository _StoreRepository;

        public EmailSenderFactory(IBackgroundJobClient jobClient,
            SettingsRepository repository,
            StoreRepository storeRepository)
        {
            _JobClient = jobClient;
            _Repository = repository;
            _StoreRepository = storeRepository;
        }

        public IEmailSender GetEmailSender(string storeId = null)
        {
            var serverSender = new ServerEmailSender(_Repository, _JobClient);
            if (string.IsNullOrEmpty(storeId))
                return serverSender;
            return new StoreEmailSender(_StoreRepository, serverSender, _JobClient, storeId);
        }
    }
}
