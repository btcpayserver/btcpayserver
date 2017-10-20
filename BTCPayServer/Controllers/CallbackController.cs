using BTCPayServer.Logging;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer;
using Microsoft.Extensions.Logging;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Controllers
{
	public class CallbackController : Controller
	{
		public class CallbackSettings
		{
			public string Token
			{
				get; set;
			}
		}
		SettingsRepository _Settings;
		Network _Network;
		InvoiceWatcher _Watcher;
		ExplorerClient _Explorer;

		public CallbackController(SettingsRepository repo,
								  ExplorerClient explorer,
								  InvoiceWatcher watcher,
								  Network network)
		{
			_Settings = repo;
			_Network = network;
			_Watcher = watcher;
			_Explorer = explorer;
		}

		[Route("callbacks/transactions")]
		[HttpPost]
		public async Task NewTransaction(string token)
		{
			await AssertToken(token);
			Logs.PayServer.LogInformation("New transaction callback");
			//We don't want to register all the json converter at MVC level, so we parse here
			var serializer = new NBXplorer.Serializer(_Network);
			var content = await new StreamReader(Request.Body, new UTF8Encoding(false), false, 1024, true).ReadToEndAsync();
			var match = serializer.ToObject<TransactionMatch>(content);

			foreach(var output in match.Outputs)
			{
				await _Watcher.NotifyReceived(output.ScriptPubKey);
			}
		}

		[Route("callbacks/blocks")]
		[HttpPost]
		public async Task NewBlock(string token)
		{
			await AssertToken(token);
			Logs.PayServer.LogInformation("New block callback");
			await _Watcher.NotifyBlock();
		}

		private async Task AssertToken(string token)
		{
			var callback = await _Settings.GetSettingAsync<CallbackSettings>();
			if(await GetToken() != token)
				throw new BTCPayServer.BitpayHttpException(400, "invalid-callback-token");
		}

		public async Task<Uri> GetCallbackUriAsync(HttpRequest request)
		{
			string token = await GetToken();
			return new Uri(request.GetAbsoluteRoot() + "/callbacks/transactions?token=" + token);
		}

		public async Task RegisterCallbackUriAsync(DerivationStrategyBase derivationScheme, HttpRequest request)
		{
			var uri = await GetCallbackUriAsync(request);
			await _Explorer.SubscribeToWalletAsync(uri, derivationScheme);
		}

		private async Task<string> GetToken()
		{
			var callback = await _Settings.GetSettingAsync<CallbackSettings>();
			if(callback == null)
			{
				callback = new CallbackSettings() { Token = Guid.NewGuid().ToString() };
				await _Settings.UpdateSetting(callback);
			}
			var token = callback.Token;
			return token;
		}

		public async Task<Uri> GetCallbackBlockUriAsync(HttpRequest request)
		{
			string token = await GetToken();
			return new Uri(request.GetAbsoluteRoot() + "/callbacks/blocks?token=" + token);
		}

		public async Task<Uri> RegisterCallbackBlockUriAsync(HttpRequest request)
		{
			var uri = await GetCallbackBlockUriAsync(request);
			await _Explorer.SubscribeToBlocksAsync(uri);
			return uri;
		}
	}
}
