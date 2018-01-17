using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBXplorer.Models;

namespace BTCPayServer.Services
{
    public class TransactionCacheProvider
    {
        IOptions<MemoryCacheOptions> _Options;
        public TransactionCacheProvider(IOptions<MemoryCacheOptions> options)
        {
            _Options = options;
        }

        ConcurrentDictionary<string, TransactionCache> _TransactionCaches = new ConcurrentDictionary<string, TransactionCache>();
        public TransactionCache GetTransactionCache(BTCPayNetwork network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            return _TransactionCaches.GetOrAdd(network.CryptoCode, c => new TransactionCache(_Options, network));
        }
    }
    public class TransactionCache : IDisposable
    {
        IOptions<MemoryCacheOptions> _Options;
        public TransactionCache(IOptions<MemoryCacheOptions> options, BTCPayNetwork network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            _Options = options;
            _MemoryCache = new MemoryCache(_Options);
            Network = network;
        }

        uint256 _LastHash;
        int _ConfOffset;
        IMemoryCache _MemoryCache;
        
        public void NewBlock(uint256 newHash, uint256 previousHash)
        {
            if (_LastHash != previousHash)
            {
                var old = _MemoryCache;
                _ConfOffset = 0;
                _MemoryCache = new MemoryCache(_Options);
                Thread.MemoryBarrier();
                old.Dispose();
            }
            else
                _ConfOffset++;
            _LastHash = newHash;
        }

        public TimeSpan CacheSpan { get; private set; } = TimeSpan.FromMinutes(60);

        public BTCPayNetwork Network { get; private set; }

        public void AddToCache(TransactionResult tx)
        {
            _MemoryCache.Set(tx.Transaction.GetHash(), tx, DateTimeOffset.UtcNow + CacheSpan);
        }


        public TransactionResult GetTransaction(uint256 txId)
        {
            _MemoryCache.TryGetValue(txId.ToString(), out object tx);

            var result = tx as TransactionResult;
            var confOffset = _ConfOffset;
            if (result != null && result.Confirmations > 0 && confOffset > 0)
            {
                var serializer = new NBXplorer.Serializer(Network.NBitcoinNetwork);
                result = serializer.ToObject<TransactionResult>(serializer.ToString(result));
                result.Confirmations += confOffset;
                result.Height += confOffset;
            }
            return result;
        }

        public void Dispose()
        {
            _MemoryCache.Dispose();
        }
    }
}
