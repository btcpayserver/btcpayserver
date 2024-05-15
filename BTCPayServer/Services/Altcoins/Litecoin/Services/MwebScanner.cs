using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcMwebdClient;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace BTCPayServer.Services.Altcoins.Litecoin.Services
{
    public class MwebScanner(PaymentMethodHandlerDictionary handlers,
                             EventAggregator eventAggregator,
                             StoreRepository storeRepository) : IHostedService
    {
        private readonly PaymentMethodHandlerDictionary _handlers = handlers;
        private readonly EventAggregator _eventAggregator = eventAggregator;
        private readonly StoreRepository _storeRepository = storeRepository;

        private readonly Dictionary<string, Task> _mwebScanners = [];
        private readonly Dictionary<string, Dictionary<string, Utxo>> _mwebUtxos = [];

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var store in await _storeRepository.GetStores())
            {
                var derivationScheme = store.GetDerivationSchemeSettings(_handlers, "LTC");
                var key = derivationScheme.AccountDerivation.ToString();
                if (_mwebScanners.ContainsKey(key)) continue;
                _mwebUtxos[key] = [];
                _mwebScanners[key] = Track(derivationScheme);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private async Task Track(DerivationSchemeSettings derivationScheme)
        {
            using var channel = GrpcChannel.ForAddress("http://localhost:12345");
            var client = new Rpc.RpcClient(channel);
            using var call = client.Utxos(new UtxosRequest {
                ScanSecret = ByteString.CopyFrom(derivationScheme.GetSigningAccountKeySettings().MwebScanKey.PrivateKey.ToBytes())
            });
            await foreach (var utxo in call.ResponseStream.ReadAllAsync())
            {
                var key = derivationScheme.AccountDerivation.ToString();
                _mwebUtxos[key][utxo.OutputId] = utxo;
            }
        }
    }
}
