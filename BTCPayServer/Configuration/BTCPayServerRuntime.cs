using BTCPayServer.Authentication;
using Microsoft.Extensions.Logging;
using BTCPayServer.Logging;
using DBreeze;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BTCPayServer.Data;
using BTCPayServer.Servcices.Invoices;
using BTCPayServer.Services.Wallets;

namespace BTCPayServer.Configuration
{
	public class BTCPayServerRuntime : IDisposable
	{
		public ExplorerClient Explorer
		{
			get;
			private set;
		}

		public void Configure(BTCPayServerOptions opts)
		{
			ConfigureAsync(opts).GetAwaiter().GetResult();
		}
		public async Task ConfigureAsync(BTCPayServerOptions opts)
		{
			Network = opts.Network;
			Explorer = new ExplorerClient(opts.Network, opts.Explorer);

			if(!Explorer.SetCookieAuth(opts.CookieFile))
				Explorer.SetNoAuth();

			CancellationTokenSource cts = new CancellationTokenSource(30000);
			try
			{
				Logs.Configuration.LogInformation("Trying to connect to explorer " + Explorer.Address.AbsoluteUri);
				await Explorer.WaitServerStartedAsync(cts.Token).ConfigureAwait(false);
				Logs.Configuration.LogInformation("Connection successfull");
			}
			catch(Exception ex)
			{
				throw new ConfigException($"Could not connect to NBXplorer, {ex.Message}");
			}
			DBreezeEngine db = new DBreezeEngine(CreateDBPath(opts, "TokensDB"));
			_Resources.Add(db);
			TokenRepository = new TokenRepository(db);

			db = new DBreezeEngine(CreateDBPath(opts, "InvoiceDB"));
			_Resources.Add(db);

			ApplicationDbContextFactory dbContext = null;
			if(opts.PostgresConnectionString == null)
			{
				var connStr = "Data Source=" + Path.Combine(opts.DataDir, "sqllite.db");
				Logs.Configuration.LogInformation($"SQLite DB used ({connStr})");
				dbContext = new ApplicationDbContextFactory(DatabaseType.Sqlite, connStr);
			}
			else
			{
				Logs.Configuration.LogInformation($"Postgres DB used ({opts.PostgresConnectionString})");
				dbContext = new ApplicationDbContextFactory(DatabaseType.Postgres, opts.PostgresConnectionString);
			}
			DBFactory = dbContext;
			InvoiceRepository = new InvoiceRepository(dbContext, db, Network);


			db = new DBreezeEngine(CreateDBPath(opts, "AddressMapping"));
			_Resources.Add(db);
			Wallet = new BTCPayWallet(Explorer, db);
		}

		private static string CreateDBPath(BTCPayServerOptions opts, string name)
		{
			var dbpath = Path.Combine(opts.DataDir, name);
			if(!Directory.Exists(dbpath))
				Directory.CreateDirectory(dbpath);
			return dbpath;
		}

		List<IDisposable> _Resources = new List<IDisposable>();

		public void Dispose()
		{
			lock(_Resources)
			{
				foreach(var r in _Resources)
				{
					r.Dispose();
				}
				_Resources.Clear();
			}
		}

		public Network Network
		{
			get;
			private set;
		}
		public TokenRepository TokenRepository
		{
			get; set;
		}
		public InvoiceRepository InvoiceRepository
		{
			get;
			set;
		}
		public BTCPayWallet Wallet
		{
			get;
			set;
		}
		public ApplicationDbContextFactory DBFactory
		{
			get;
			set;
		}
	}

}
