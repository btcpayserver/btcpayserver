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
		private DerivationStrategyFactory _DerivationStrategyFactory;
		ApplicationDbContextFactory _DBFactory;

		public BTCPayWallet(ExplorerClient client, ApplicationDbContextFactory factory)
		{
			if(client == null)
				throw new ArgumentNullException(nameof(client));
			if(factory == null)
				throw new ArgumentNullException(nameof(factory));
			_Client = client;
			_DBFactory = factory;
			_Serializer = new NBXplorer.Serializer(_Client.Network);
			_DerivationStrategyFactory = new DerivationStrategyFactory(_Client.Network);
		}


		public async Task<BitcoinAddress> ReserveAddressAsync(string walletIdentifier)
		{
			var pathInfo = await _Client.GetUnusedAsync(_DerivationStrategyFactory.Parse(walletIdentifier), DerivationFeature.Deposit, 0, true).ConfigureAwait(false);
			return pathInfo.ScriptPubKey.GetDestinationAddress(_DerivationStrategyFactory.Network);
		}

		public Task TrackAsync(string walletIdentifier)
		{
			return _Client.TrackAsync(_DerivationStrategyFactory.Parse(walletIdentifier));
		}

		public async Task<string> GetInvoiceId(Script scriptPubKey)
		{
			using(var db = _DBFactory.CreateContext())
			{
				var result = await db.AddressInvoices.FindAsync(scriptPubKey.Hash.ToString());
				return result?.InvoiceDataId;
			}
		}

		public async Task MapAsync(Script address, string invoiceId)
		{
			using(var db = _DBFactory.CreateContext())
			{
				db.AddressInvoices.Add(new AddressInvoiceData()
				{
					Address = address.Hash.ToString(),
					InvoiceDataId = invoiceId
				});
				await db.SaveChangesAsync();
			}
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
