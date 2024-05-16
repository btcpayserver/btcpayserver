using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcMwebdClient;
using Microsoft.Extensions.Hosting;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Altcoins.Litecoin.Services
{
    public class MwebScannerService(
        PaymentMethodHandlerDictionary handlers,
        EventAggregator eventAggregator,
        InvoiceRepository invoiceRepository,
        StoreRepository storeRepository) : IHostedService
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Dictionary<DerivationStrategyBase, Scanner> _scanners = [];

        private class Scanner
        {
            public Task Task { get; }
            public Dictionary<string, Utxo> Utxos { get; } = [];
            private readonly CancellationToken _cancellationToken;

            public Scanner(DerivationSchemeSettings derivationScheme,
                           CancellationToken cancellationToken)
            {
                _cancellationToken = cancellationToken;
                Task = Scan(derivationScheme);
            }

            private async Task Scan(DerivationSchemeSettings derivationScheme)
            {
                using var channel = GrpcChannel.ForAddress("http://localhost:12345");
                var client = new Rpc.RpcClient(channel);
                using var call = client.Utxos(new UtxosRequest {
                    ScanSecret = ByteString.CopyFrom(derivationScheme.GetSigningAccountKeySettings().MwebScanKey.PrivateKey.ToBytes())
                });
                await foreach (var utxo in call.ResponseStream.ReadAllAsync(_cancellationToken))
                {
                    Utxos[utxo.OutputId] = utxo;
                }
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var store in await storeRepository.GetStores())
            {
                var derivationScheme = store.GetDerivationSchemeSettings(handlers, "LTC");
                if (_scanners.ContainsKey(derivationScheme.AccountDerivation)) continue;
                _scanners[derivationScheme.AccountDerivation] = new Scanner(derivationScheme, _cts.Token);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cts.Cancel();
            foreach (var scanner in _scanners.Values)
            {
                try
                {
                    await scanner.Task.WaitAsync(cancellationToken);
                }
                catch (RpcException e) when (e.InnerException is OperationCanceledException)
                {
                }
            }
        }
    }
}
