using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcMwebdClient;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Altcoins.Litecoin.Services
{
    public class MwebScannerService(
        PaymentMethodHandlerDictionary handlers,
        BTCPayNetworkProvider networks,
        EventAggregator eventAggregator,
        InvoiceRepository invoiceRepository,
        PaymentService paymentService,
        WalletRepository walletRepository,
        StoreRepository storeRepository) : IHostedService
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<DerivationStrategyBase, Scanner> _scanners = [];
        private readonly CompositeDisposable _leases = new();

        private class Scanner
        {
            public Task Task { get; }
            public DerivationSchemeSettings DerivationScheme { get; }
            public ConcurrentDictionary<string, Utxo> Utxos { get; } = [];
            private readonly MwebScannerService _service;

            public Scanner(MwebScannerService service, DerivationSchemeSettings derivationScheme)
            {
                DerivationScheme = derivationScheme;
                _service = service;
                Task = Start();
            }

            private async Task Start()
            {
                using var channel = GrpcChannel.ForAddress("http://localhost:12345");
                var client = new Rpc.RpcClient(channel);
                using var call = client.Utxos(new UtxosRequest
                {
                    ScanSecret = ByteString.CopyFrom(DerivationScheme.GetSigningAccountKeySettings().MwebScanKey.PrivateKey.ToBytes())
                });
                await foreach (var utxo in call.ResponseStream.ReadAllAsync(_service._cts.Token))
                {
                    if (utxo.OutputId == "") continue;
                    Utxos[utxo.OutputId] = utxo;
                    var height = (await client.StatusAsync(new StatusRequest())).BlockHeaderHeight;
                    await _service.CheckInvoice(DerivationScheme, utxo, height);
                    await _service.PublishEvent(DerivationScheme, utxo, height);
                }
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _leases.Add(eventAggregator.SubscribeAsync<Events.NewBlockEvent>(async evt =>
            {
                var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("LTC");
                if (evt.PaymentMethodId == pmi) await UpdatePaymentStates(((NBXplorer.Models.NewBlockEvent)evt.AdditionalInfo).Height);
            }));

            foreach (var store in await storeRepository.GetStores())
            {
                var derivationScheme = store.GetDerivationSchemeSettings(handlers, "MWEB");
                if (derivationScheme == null) continue;
                if (_scanners.ContainsKey(derivationScheme.AccountDerivation)) continue;
                _scanners[derivationScheme.AccountDerivation] = new Scanner(this, derivationScheme);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _leases.Dispose();
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

        public bool IsAddressUsed(DerivationStrategyBase derivationStrategy, string address)
        {
            return _scanners[derivationStrategy].Utxos.Values.Any(utxo => utxo.Address == address);
        }

        public async Task<ReceivedCoin[]> GetUnspentCoins(
            DerivationStrategyBase derivationStrategy,
            bool excludeUnconfirmed = false)
        {
            await StartAsync(_cts.Token);
            if (!_scanners.TryGetValue(derivationStrategy, out var scanner)) return [];
            using var channel = GrpcChannel.ForAddress("http://localhost:12345");
            var client = new Rpc.RpcClient(channel);
            var response = await client.SpentAsync(new SpentRequest
            {
                OutputId = {scanner.Utxos.Keys}
            });
            foreach (var outputId in response.OutputId)
            {
                scanner.Utxos.Remove(outputId, out _);
            }
            var height = (await client.StatusAsync(new StatusRequest())).BlockHeaderHeight;
            var coins = new List<ReceivedCoin>();
            foreach (var utxo in scanner.Utxos.Values)
            {
                if (excludeUnconfirmed && utxo.Height == 0) continue;
                var coin = await CoinFromUtxo(scanner.DerivationScheme, utxo, height);
                if (coin != null) coins.Add(coin);
            }
            return [..coins];
        }

        private async Task<ReceivedCoin> CoinFromUtxo(
            DerivationSchemeSettings derivationScheme, Utxo utxo, int height)
        {
            var network = networks.GetNetwork<BTCPayNetwork>("MWEB");
            var bech32 = new MwebBech32Encoder();
            var wos = await walletRepository.GetWalletObjects(new GetWalletObjectsQuery(null, WalletObjectData.Types.Address, [utxo.Address]));
            if (wos.Count == 0) return null;
            var data = JObject.Parse(wos.First().Value.Data);
            var addressIndex = data["addressIndex"]?.Value<uint>() ?? 0;
            return new ReceivedCoin
            {
                ScriptPubKey = new Script(bech32.Decode(utxo.Address, out _)),
                OutPoint = OutPoint.Parse($"{utxo.OutputId}:{addressIndex}"),
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(utxo.BlockTime),
                KeyPath = new KeyPath([addressIndex]),
                Value = new Money(utxo.Value),
                Confirmations = utxo.Height > 0 ? height - utxo.Height + 1 : 0,
                Address = new BitcoinMwebAddress(utxo.Address, network.NBitcoinNetwork)
            };
        }

        private async Task CheckInvoice(DerivationSchemeSettings derivationScheme, Utxo utxo, int height)
        {
            var network = networks.GetNetwork<BTCPayNetwork>("MWEB");
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            var coin = await CoinFromUtxo(derivationScheme, utxo, height);
            if (coin == null) return;
            var key = network.GetTrackedDestination(coin.ScriptPubKey);
            var invoice = await invoiceRepository.GetInvoiceFromAddress(pmi, key);
            if (invoice != null)
            {
                var details = new BitcoinLikePaymentData(coin.OutPoint, false, coin.KeyPath)
                {
                    ConfirmationCount = coin.Confirmations
                };

                var paymentData = new PaymentData()
                {
                    Id = coin.OutPoint.ToString(),
                    Created = DateTimeOffset.UtcNow,
                    Status = NBXplorerListener.ConfirmationRequired(invoice, details) <= details.ConfirmationCount ? PaymentStatus.Settled : PaymentStatus.Processing,
                    Amount = ((Money)coin.Value).ToDecimal(MoneyUnit.BTC),
                    Currency = network.Currency
                }.Set(invoice, handlers[pmi], details);

                var alreadyExist = invoice.GetPayments(false).Any(c => c.Id == paymentData.Id && c.PaymentMethodId == pmi);
                if (!alreadyExist)
                {
                    var payment = await paymentService.AddPayment(paymentData, [coin.OutPoint.Hash.ToString()]);
                    if (payment != null) await ReceivedPayment(invoice, payment, height);
                }
                else
                {
                    await UpdatePaymentStates(invoice, height);
                }
            }
        }

        private async Task PublishEvent(DerivationSchemeSettings derivationScheme, Utxo utxo, int height)
        {
            var coin = await CoinFromUtxo(derivationScheme, utxo, height);
            if (coin == null) return;
            var transaction = Transaction.Create(coin.Address.Network);
            transaction.Outputs.Add((Money)coin.Value, coin.ScriptPubKey);
            eventAggregator.Publish(new NewOnChainTransactionEvent
            {
                PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId("MWEB"),
                NewTransactionEvent = new NewTransactionEvent
                {
                    DerivationStrategy = derivationScheme.AccountDerivation,
                    TransactionData = new TransactionResult
                    {
                        Confirmations = coin.Confirmations,
                        TransactionHash = coin.OutPoint.Hash,
                        Transaction = transaction,
                        Height = utxo.Height > 0 ? utxo.Height : null,
                        Timestamp = coin.Timestamp,
                    },
                    Outputs = [new MatchedOutput
                    {
                        KeyPath = coin.KeyPath,
                        ScriptPubKey = coin.ScriptPubKey,
                        Index = 0,
                        Value = coin.Value,
                        Address = coin.Address,
                    }],
                }
            });
        }

        private async Task<InvoiceEntity> ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment, int height)
        {
            // We want the invoice watcher to look at our invoice after we bumped the payment method fee, so fireEvent=false.
            invoice = await UpdatePaymentStates(invoice, height, fireEvents: false);
            if (invoice == null) return null;
            var prompt = invoice.GetPaymentPrompt(payment.PaymentMethodId);
            var bitcoinPaymentMethod = (BitcoinPaymentPromptDetails)handlers.ParsePaymentPromptDetails(prompt);
            if (bitcoinPaymentMethod.FeeMode == NetworkFeeMode.MultiplePaymentsOnly && prompt.PaymentMethodFee == 0.0m)
            {
                prompt.PaymentMethodFee = bitcoinPaymentMethod.PaymentMethodFeeRate.GetFee(100).ToDecimal(MoneyUnit.BTC); // assume price for 100 bytes
                await invoiceRepository.UpdatePrompt(invoice.Id, prompt);
                invoice = await invoiceRepository.GetInvoice(invoice.Id);
            }
            eventAggregator.Publish(new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
            return invoice;
        }

        private async Task UpdatePaymentStates(int height)
        {
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("MWEB");
            var invoices = await invoiceRepository.GetMonitoredInvoices(pmi);
            await Task.WhenAll(invoices.Select(i => UpdatePaymentStates(i, height)).ToArray());
        }

        private async Task<InvoiceEntity> UpdatePaymentStates(InvoiceEntity invoice, int height, bool fireEvents = true)
        {
            var network = networks.GetNetwork<BTCPayNetwork>("MWEB");
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            var handler = (BitcoinLikePaymentHandler)handlers[pmi];
            var strategy = handlers.GetDerivationStrategy(invoice, network);
            var updatedPaymentEntities = new List<PaymentEntity>();

            foreach (var payment in invoice.GetPayments(false).Where(p => p.PaymentMethodId == pmi))
            {
                var paymentData = handler.ParsePaymentDetails(payment.Details);
                if (!_scanners.TryGetValue(strategy, out var scanner)) continue;
                if (!scanner.Utxos.TryGetValue(paymentData.Outpoint.Hash.ToString(), out var utxo)) continue;
                var confirmations = utxo.Height > 0 ? height - utxo.Height + 1 : 0;
                bool updated = false;
                if (paymentData.ConfirmationCount != confirmations)
                {
                    var oldConfCount = paymentData.ConfirmationCount;
                    paymentData.ConfirmationCount = Math.Min(confirmations, network.MaxTrackedConfirmation);
                    if (oldConfCount != paymentData.ConfirmationCount)
                    {
                        payment.SetDetails(handler, paymentData);
                        updated = true;
                    }
                }

                var prevStatus = payment.Status;
                payment.Status = NBXplorerListener.ConfirmationRequired(invoice, paymentData) <= paymentData.ConfirmationCount ? PaymentStatus.Settled : PaymentStatus.Processing;
                updated |= prevStatus != payment.Status;
                if (updated) updatedPaymentEntities.Add(payment);
            }

            await paymentService.UpdatePayments(updatedPaymentEntities);
            if (fireEvents && updatedPaymentEntities.Count != 0)
                eventAggregator.Publish(new InvoiceNeedUpdateEvent(invoice.Id));
            return invoice;
        }
    }
}
