using NBXplorer;
using Microsoft.Extensions.Logging;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using BTCPayServer.Logging;
using System.Threading;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using Hangfire;
using BTCPayServer.Services.Wallets;

namespace BTCPayServer.Servcices.Invoices
{
	public class InvoiceWatcher : IHostedService
	{
		InvoiceRepository _InvoiceRepository;
		ExplorerClient _ExplorerClient;
		DerivationStrategyFactory _DerivationFactory;
		InvoiceNotificationManager _NotificationManager;
		BTCPayWallet _Wallet;

		public InvoiceWatcher(ExplorerClient explorerClient, 
			InvoiceRepository invoiceRepository,
			BTCPayWallet wallet,
			InvoiceNotificationManager notificationManager)
		{
			_Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
			_ExplorerClient = explorerClient ?? throw new ArgumentNullException(nameof(explorerClient));
			_DerivationFactory = new DerivationStrategyFactory(_ExplorerClient.Network);
			_InvoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
			_NotificationManager = notificationManager ?? throw new ArgumentNullException(nameof(notificationManager));
		}

		private async Task StartWatchInvoice(string invoiceId)
		{
			Logs.PayServer.LogInformation("Watching invoice " + invoiceId);
			UTXOChanges changes = null;
			while(true)
			{
				try
				{
					var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId).ConfigureAwait(false);
					if(invoice == null)
						break;
					var stateBefore = invoice.Status;
					var result = await UpdateInvoice(changes, invoice).ConfigureAwait(false);
					changes = result.Changes;
					if(result.NeedSave)
						await _InvoiceRepository.UpdateInvoiceStatus(invoice.Id, invoice.Status, invoice.ExceptionStatus).ConfigureAwait(false);

					if(stateBefore != invoice.Status)
					{
						Logs.PayServer.LogInformation($"Invoice {invoice.Id}: {stateBefore} => {invoice.Status}");
					}

					if(invoice.Status == "complete" || invoice.Status == "invalid")
					{
						await _InvoiceRepository.RemovePendingInvoice(invoice.Id).ConfigureAwait(false);
						Logs.PayServer.LogInformation("Stopped watching invoice " + invoiceId);
						break;
					}
				}
				catch(OperationCanceledException) when(_Cts.Token.IsCancellationRequested)
				{
					break;
				}
				catch(Exception ex)
				{
					Logs.PayServer.LogError(ex, "Unhandled error on watching invoice " + invoiceId);
					await Task.Delay(10000, _Cts.Token).ConfigureAwait(false);
				}
			}
		}


		private async Task<(bool NeedSave, UTXOChanges Changes)> UpdateInvoice(UTXOChanges changes, InvoiceEntity invoice)
		{			
			if(invoice.Status == "invalid")
			{
				return (false, changes);
			}
			bool needSave = false;
			bool shouldWait = true;

			if(invoice.ExpirationTime < DateTimeOffset.UtcNow && (invoice.Status == "new" || invoice.Status == "paidPartial"))
			{
				needSave = true;
				invoice.Status = "invalid";
			}

			if(invoice.Status == "new" || invoice.Status == "paidPartial")
			{
				var strategy = _DerivationFactory.Parse(invoice.DerivationStrategy);
				changes = await _ExplorerClient.SyncAsync(strategy, changes, false, _Cts.Token).ConfigureAwait(false);

				var utxos = changes.Confirmed.UTXOs.Concat(changes.Unconfirmed.UTXOs).ToArray();
				var invoiceIds = utxos.Select(u => _Wallet.GetInvoiceId(u.Output.ScriptPubKey)).ToArray();
				utxos =
					utxos
					.Where((u,i) => invoiceIds[i].GetAwaiter().GetResult() == invoice.Id)
					.ToArray();

				shouldWait = false; //should not wait, Sync is blocking call

				List<Coin> receivedCoins = new List<Coin>();
				foreach(var received in utxos)
					if(received.Output.ScriptPubKey == invoice.DepositAddress.ScriptPubKey)
						receivedCoins.Add(new Coin(received.Outpoint, received.Output));

				var alreadyAccounted = new HashSet<OutPoint>(invoice.Payments.Select(p => p.Outpoint));
				foreach(var coin in receivedCoins.Where(c => !alreadyAccounted.Contains(c.Outpoint)))
				{
					var payment = await _InvoiceRepository.AddPayment(invoice.Id, coin).ConfigureAwait(false);
					invoice.Payments.Add(payment);
					if(invoice.Status == "new")
					{
						invoice.Status = "paidPartial";
						needSave = true;
					}
				}
			}

			if(invoice.Status == "paidPartial")
			{
				var totalPaid = invoice.Payments.Select(p => p.Output.Value).Sum();
				if(totalPaid == invoice.GetTotalCryptoDue())
				{
					invoice.Status = "paid";
					if(invoice.FullNotifications)
					{
						_NotificationManager.Notify(invoice);
					}
					invoice.ExceptionStatus = null;
					needSave = true;
				}

				if(totalPaid > invoice.GetTotalCryptoDue())
				{
					invoice.Status = "paidOver";
					invoice.ExceptionStatus = "paidOver";
					needSave = true;
				}

				if(totalPaid < invoice.GetTotalCryptoDue() && invoice.ExceptionStatus == null)
				{
					invoice.ExceptionStatus = "paidPartial";
					needSave = true;
				}
			}

			if(invoice.Status == "paid" || invoice.Status == "paidOver")
			{
				var getTransactions = invoice.Payments.Select(o => o.Outpoint.Hash).Select(o => _ExplorerClient.GetTransactionAsync(o, _Cts.Token)).ToArray();
				await Task.WhenAll(getTransactions).ConfigureAwait(false);
				var transactions = getTransactions.Select(c => c.GetAwaiter().GetResult()).ToArray();

				bool confirmed = false;
				var minConf = transactions.Select(t => t.Confirmations).Min();
				if(invoice.SpeedPolicy == SpeedPolicy.HighSpeed)
				{
					if(minConf > 0)
						confirmed = true;
					else
						confirmed = !transactions.Any(t => t.Transaction.RBF);
				}
				else if(invoice.SpeedPolicy == SpeedPolicy.MediumSpeed)
				{
					confirmed = minConf >= 1;
				}
				else if(invoice.SpeedPolicy == SpeedPolicy.LowSpeed)
				{
					confirmed = minConf >= 6;
				}

				if(confirmed)
				{
					invoice.Status = "confirmed";
					_NotificationManager.Notify(invoice);
					needSave = true;
				}
			}

			if(invoice.Status == "confirmed")
			{
				var getTransactions = invoice.Payments.Select(o => o.Outpoint.Hash).Select(o => _ExplorerClient.GetTransactionAsync(o, _Cts.Token)).ToArray();
				await Task.WhenAll(getTransactions).ConfigureAwait(false);
				var transactions = getTransactions.Select(c => c.GetAwaiter().GetResult()).ToArray();
				var minConf = transactions.Select(t => t.Confirmations).Min();
				if(minConf >= 6)
				{
					invoice.Status = "complete";
					if(invoice.FullNotifications)
						_NotificationManager.Notify(invoice);
					needSave = true;
				}
			}

			shouldWait = shouldWait && !needSave;

			if(shouldWait)
			{
				await Task.Delay(PollInterval, _Cts.Token).ConfigureAwait(false);
			}

			return (needSave, changes);
		}

		public TimeSpan PollInterval
		{
			get; set;
		} = TimeSpan.FromSeconds(10);

		public async Task WatchAsync(string invoiceId)
		{
			await _InvoiceRepository.AddPendingInvoice(invoiceId).ConfigureAwait(false);
			_WatchRequests.Add(invoiceId);
		}

		BlockingCollection<string> _WatchRequests = new BlockingCollection<string>(new ConcurrentQueue<string>());

		public void Dispose()
		{
			_Cts.Cancel();
		}


		Thread _Thread;
		TaskCompletionSource<bool> _RunningTask;
		CancellationTokenSource _Cts;
		public Task StartAsync(CancellationToken cancellationToken)
		{
			foreach(var pending in _InvoiceRepository.GetPendingInvoices())
			{
				_WatchRequests.Add(pending);
			}
			_RunningTask = new TaskCompletionSource<bool>();
			_Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			_Thread = new Thread(Run) { Name = "InvoiceWatcher" };
			_Thread.Start();
			return Task.CompletedTask;
		}

		void Run()
		{
			Logs.PayServer.LogInformation("Start watching invoices");
			List<Task> watching = new List<Task>();
			try
			{
				foreach(var item in _WatchRequests.GetConsumingEnumerable(_Cts.Token))
				{
					watching.Add(StartWatchInvoice(item));
					foreach(var task in watching.ToList())
					{
						if(task.Status != TaskStatus.Running)
						{
							watching.Remove(task);
						}
					}
				}
			}
			catch(OperationCanceledException) when(_Cts.Token.IsCancellationRequested)
			{
				try
				{
					Task.WaitAll(watching.ToArray());
				}
				catch(AggregateException) { }
				_RunningTask.TrySetResult(true);
			}
			catch(Exception ex)
			{
				_Cts.Cancel();
				_RunningTask.TrySetException(ex);
				Logs.PayServer.LogCritical(ex, "Error in the InvoiceWatcher loop");
			}
			finally
			{
				Logs.PayServer.LogInformation("Stop watching invoices");
			}
		}


		public Task StopAsync(CancellationToken cancellationToken)
		{
			_Cts.Cancel();
			return Task.WhenAny(_RunningTask.Task, Task.Delay(-1, cancellationToken));
		}
	}
}
