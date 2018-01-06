using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;

namespace BTCPayServer.Services.Wallets
{
    public class BTCPayWallet
    {
        private ExplorerClient _Client;
        private Serializer _Serializer;
        ApplicationDbContextFactory _DBFactory;

        public BTCPayWallet(ExplorerClient client, ApplicationDbContextFactory factory)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            _Client = client;
            _DBFactory = factory;
            _Serializer = new NBXplorer.Serializer(_Client.Network);
        }


        public async Task<BitcoinAddress> ReserveAddressAsync(DerivationStrategy derivationStrategy)
        {
            var pathInfo = await _Client.GetUnusedAsync(derivationStrategy.DerivationStrategyBase, DerivationFeature.Deposit, 0, true).ConfigureAwait(false);
            return pathInfo.ScriptPubKey.GetDestinationAddress(_Client.Network);
        }

        public async Task TrackAsync(DerivationStrategy derivationStrategy)
        {
            await _Client.TrackAsync(derivationStrategy.DerivationStrategyBase);
        }

        private byte[] ToBytes<T>(T obj)
        {
            return ZipUtils.Zip(_Serializer.ToString(obj));
        }

        public Task BroadcastTransactionsAsync(List<Transaction> transactions)
        {
            var tasks = transactions.Select(t => _Client.BroadcastAsync(t)).ToArray();
            return Task.WhenAll(tasks);
        }

        public async Task<Money> GetBalance(DerivationStrategy derivationStrategy)
        {
            var result = await _Client.SyncAsync(derivationStrategy.DerivationStrategyBase, null, true);
            return result.Confirmed.UTXOs.Select(u => u.Value)
                         .Concat(result.Unconfirmed.UTXOs.Select(u => u.Value))
                         .Sum();
        }
    }
}
