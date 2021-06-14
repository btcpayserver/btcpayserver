using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using Microsoft.Extensions.Caching.Memory;

namespace BTCPayServer.Plugins.CoinSwitch
{
    public class CoinSwitchService: IDisposable
    {
        private readonly IBTCPayServerClientFactory _btcPayServerClientFactory;
        private readonly IMemoryCache _memoryCache;
        private BTCPayServerClient _client;

        public CoinSwitchService(IBTCPayServerClientFactory btcPayServerClientFactory,IMemoryCache memoryCache)
        {
            _btcPayServerClientFactory = btcPayServerClientFactory;
            _memoryCache = memoryCache;
        }
        
        public async Task<CoinSwitchSettings> GetCoinSwitchForInvoice(string id)
        {

            _client ??= await _btcPayServerClientFactory.Create("");
            return await _memoryCache.GetOrCreateAsync($"{nameof(CoinSwitchService)}-{id}", async entry =>
            {
                try
                {

                    var i = await _client.AdminGetInvoice(id);
                }
                catch (Exception e)
                {
                    return null;
                }
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                var d = await _storeRepository.GetStoreByInvoiceId(id);
                
                return d?.GetStoreBlob()?.GetCoinSwitchSettings();
            });
        }

        public void Dispose()
        {
            _memoryCache?.Dispose();
        }
    }
}
