using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Services;
using Microsoft.Extensions.Caching.Memory;
using Zammad.Client;

namespace BTCPayServer.Zammad
{
    public class ZammadHostedService : EventHostedServiceBase
    {
        private readonly SettingsRepository _settingsRepository;
        private readonly IMemoryCache _memoryCache;

        public ZammadHostedService(EventAggregator eventAggregator, SettingsRepository settingsRepository,
            IMemoryCache memoryCache) : base(
            eventAggregator)
        {
            _settingsRepository = settingsRepository;
            _memoryCache = memoryCache;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await base.StartAsync(cancellationToken);
            var setting = await _settingsRepository.GetSettingAsync<ZammadOptions>();
            _memoryCache.Set(nameof(ZammadOptions), setting);
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<UserRegisteredEvent>();
            Subscribe<SettingsChanged<ZammadOptions>>();
            base.SubscribeToEvents();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is EmailChangedEvent emailChangedEvent)
            {
                var setting = await _settingsRepository.GetSettingAsync<ZammadOptions>();
                if (setting?.Configured is true)
                {
                    var client = ZammadAccount.CreateTokenAccount(setting.Endpoint, setting.APIKey);
                    var userClient = client.CreateUserClient();

                    var matchedUser = (await userClient.SearchUserAsync(emailChangedEvent.UserId, 1)).FirstOrDefault();
                    if (matchedUser != null)
                    {
                        matchedUser.Email = emailChangedEvent.Email;
                        await userClient.UpdateUserAsync(matchedUser.Id, matchedUser);
                    }
                }
            }

            if (evt is SettingsChanged<ZammadOptions> settingsChanged)
            {
                _memoryCache.Set(nameof(ZammadOptions), settingsChanged.Settings);
            }

            await base.ProcessEvent(evt, cancellationToken);
        }
    }
}
