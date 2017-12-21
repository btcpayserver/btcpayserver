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
using BTCPayServer.Configuration;
using BTCPayServer.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Hosting.Server;

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
        ExplorerClient _Explorer;
        BTCPayServerOptions _Options;
        EventAggregator _EventAggregator;
        IServer _Server;
        public CallbackController(SettingsRepository repo,
                                  ExplorerClient explorer,
                                  EventAggregator eventAggregator,
                                  BTCPayServerOptions options,
                                  IServer server,
                                  BTCPayNetworkProvider networkProvider)
        {
            _Settings = repo;
            _Network = networkProvider.GetNetwork("BTC").NBitcoinNetwork;
            _Explorer = explorer;
            _Options = options;
            _EventAggregator = eventAggregator;
            _Server = server;
        }

        [Route("callbacks/transactions")]
        [HttpPost]
        public async Task NewTransaction(string token)
        {
            await AssertToken(token);
            //We don't want to register all the json converter at MVC level, so we parse here
            var serializer = new NBXplorer.Serializer(_Network);
            var content = await new StreamReader(Request.Body, new UTF8Encoding(false), false, 1024, true).ReadToEndAsync();
            var match = serializer.ToObject<TransactionMatch>(content);

            foreach (var output in match.Outputs)
            {
                var evt = new TxOutReceivedEvent();
                evt.ScriptPubKey = output.ScriptPubKey;
                evt.Address = output.ScriptPubKey.GetDestinationAddress(_Network);
                _EventAggregator.Publish(evt);
            }
        }

        [Route("callbacks/blocks")]
        [HttpPost]
        public async Task NewBlock(string token)
        {
            await AssertToken(token);
            _EventAggregator.Publish(new NewBlockEvent());
        }

        private async Task AssertToken(string token)
        {
            var callback = await _Settings.GetSettingAsync<CallbackSettings>();
            if (await GetToken() != token)
                throw new BTCPayServer.BitpayHttpException(400, "invalid-callback-token");
        }

        public async Task<Uri> GetCallbackUriAsync()
        {
            string token = await GetToken();
            return BuildCallbackUri("callbacks/transactions?token=" + token);
        }

        public async Task RegisterCallbackUriAsync(DerivationStrategyBase derivationScheme)
        {
            var uri = await GetCallbackUriAsync();
            await _Explorer.SubscribeToWalletAsync(uri, derivationScheme);
        }

        private async Task<string> GetToken()
        {
            var callback = await _Settings.GetSettingAsync<CallbackSettings>();
            if (callback == null)
            {
                callback = new CallbackSettings() { Token = Guid.NewGuid().ToString() };
                await _Settings.UpdateSetting(callback);
            }
            var token = callback.Token;
            return token;
        }

        public async Task<Uri> GetCallbackBlockUriAsync()
        {
            string token = await GetToken();
            return BuildCallbackUri("callbacks/blocks?token=" + token);
        }

        private Uri BuildCallbackUri(string callbackPath)
        {
            var address = _Server.Features.Get<IServerAddressesFeature>().Addresses
                                 .Select(c => new Uri(TransformToRoutable(c)))
                                 .First();
            var baseUrl = _Options.InternalUrl == null ? address.AbsoluteUri : _Options.InternalUrl.AbsoluteUri;
            baseUrl = baseUrl.WithTrailingSlash();
            return new Uri(baseUrl + callbackPath);
        }

        private string TransformToRoutable(string host)
        {
            if (host.StartsWith("http://0.0.0.0"))
                host = host.Replace("http://0.0.0.0", "http://127.0.0.1");
            return host;
        }

        public async Task<Uri> RegisterCallbackBlockUriAsync(Uri uri)
        {
            await _Explorer.SubscribeToBlocksAsync(uri);
            return uri;
        }
    }
}
