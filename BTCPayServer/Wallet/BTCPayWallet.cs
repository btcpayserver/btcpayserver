using DBreeze;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Wallet
{
    public class BTCPayWallet
    {
		private ExplorerClient _Client;
		private DBreezeEngine _Engine;
		private Serializer _Serializer;
		private DerivationStrategyFactory _DerivationStrategyFactory;

		public BTCPayWallet(ExplorerClient client, DBreezeEngine dbreeze)
		{
			if(client == null)
				throw new ArgumentNullException(nameof(client));
			if(dbreeze == null)
				throw new ArgumentNullException(nameof(dbreeze));
			_Client = client;
			_Engine = dbreeze;
			_Serializer = new NBXplorer.Serializer(_Client.Network);
			_DerivationStrategyFactory = new DerivationStrategyFactory(_Client.Network);
		}

		
		public async Task<BitcoinAddress> ReserveAddressAsync(string walletIdentifier)
		{
			var pathInfo = await _Client.GetUnusedAsync(_DerivationStrategyFactory.Parse(walletIdentifier), DerivationFeature.Deposit, 0, true).ConfigureAwait(false);
			using(var tx = _Engine.GetTransaction())
			{
				var pathInfoBytes = ToBytes(pathInfo);
				tx.Insert(AddressToKeyInfo, pathInfo.Address.ToString(), pathInfoBytes);
				tx.Commit();
			}
			return pathInfo.Address;
		}

		public async Task TrackAsync(string walletIdentifier)
		{
			await _Client.SyncAsync(_DerivationStrategyFactory.Parse(walletIdentifier), null, null, true).ConfigureAwait(false);
		}

		const string AddressToId = "AtI";
		const string AddressToKeyInfo = "AtK";
		public Task MapAsync(BitcoinAddress address, string id)
		{
			using(var tx = _Engine.GetTransaction())
			{
				tx.Insert(AddressToId, address.ToString(), id);
				tx.Commit();
			}
			return Task.FromResult(true);
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
	}
}
