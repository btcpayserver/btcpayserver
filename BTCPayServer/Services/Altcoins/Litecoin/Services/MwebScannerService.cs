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
using NBXplorer.DerivationStrategy;
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
        StoreRepository storeRepository) : IHostedService
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<DerivationStrategyBase, Scanner> _scanners = [];

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
                using var call = client.Utxos(new UtxosRequest {
                    ScanSecret = ByteString.CopyFrom(DerivationScheme.GetSigningAccountKeySettings().MwebScanKey.PrivateKey.ToBytes())
                });
                await foreach (var utxo in call.ResponseStream.ReadAllAsync(_service._cts.Token))
                {
                    Utxos[utxo.OutputId] = utxo;
                    await _service.CheckInvoice(DerivationScheme, utxo);
                }
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var store in await storeRepository.GetStores())
            {
                var derivationScheme = store.GetDerivationSchemeSettings(handlers, "LTC");
                if (_scanners.ContainsKey(derivationScheme.AccountDerivation)) continue;
                _scanners[derivationScheme.AccountDerivation] = new Scanner(this, derivationScheme);
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

        private static async Task<int> GetHeight()
        {
            using var channel = GrpcChannel.ForAddress("http://localhost:12345");
            var client = new Rpc.RpcClient(channel);
            var response = await client.StatusAsync(new StatusRequest());
            return response.BlockHeaderHeight;
        }

        public async Task<ReceivedCoin[]> GetUnspentCoins(
            DerivationStrategyBase derivationStrategy,
            bool excludeUnconfirmed = false)
        {
            if (!_scanners.TryGetValue(derivationStrategy, out var scanner)) return [];
            var height = await GetHeight();
            var coins = new List<ReceivedCoin>();
            foreach (var utxo in scanner.Utxos.Values)
            {
                if (excludeUnconfirmed && utxo.Height == 0) continue;
                var coin = CoinFromUtxo(scanner.DerivationScheme, utxo, height);
                if (coin != null) coins.Add(coin);
            }
            return [..coins];
        }

        private ReceivedCoin CoinFromUtxo(DerivationSchemeSettings derivationScheme,
                                          Utxo utxo, int height)
        {
            var network = networks.GetNetwork<BTCPayNetwork>("LTC");
            var bech32 = new MwebBech32Encoder();
            return new ReceivedCoin
            {
                ScriptPubKey = new Script(bech32.Decode(utxo.Address, out _)),
                OutPoint = OutPoint.Parse($"{utxo.OutputId}:{0}"),
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(utxo.BlockTime),
                KeyPath = derivationScheme.GetSigningAccountKeySettings().AccountKeyPath,
                Value = new Money(utxo.Value),
                Confirmations = utxo.Height > 0 ? height - utxo.Height + 1 : 0,
                Address = new BitcoinMwebAddress(utxo.Address, network.NBitcoinNetwork)
            };
        }

        private async Task CheckInvoice(DerivationSchemeSettings derivationScheme, Utxo utxo)
        {
            var network = networks.GetNetwork<BTCPayNetwork>("LTC");
            var coin = CoinFromUtxo(derivationScheme, utxo, await GetHeight());
            if (coin == null) return;
            var key = network.GetTrackedDestination(coin.ScriptPubKey);
            var invoice = (await invoiceRepository.GetInvoicesFromAddresses([key])).FirstOrDefault();
            if (invoice != null)
            {
                var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
                var details = new BitcoinLikePaymentData(coin.OutPoint, false, coin.KeyPath)
                {
                    ConfirmationCount = coin.Confirmations
                };

                var paymentData = new PaymentData()
                {
                    Id = coin.OutPoint.ToString(),
                    Created = DateTimeOffset.UtcNow,
                    Status = NBXplorerListener.IsSettled(invoice, details) ? PaymentStatus.Settled : PaymentStatus.Processing,
                    Amount = ((Money)coin.Value).ToDecimal(MoneyUnit.BTC),
                    Currency = network.CryptoCode
                }.Set(invoice, handlers[pmi], details);

                var alreadyExist = invoice.GetPayments(false).Any(c => c.Id == paymentData.Id && c.PaymentMethodId == pmi);
                if (!alreadyExist)
                {
                    var payment = await paymentService.AddPayment(paymentData, [coin.OutPoint.Hash.ToString()]);
                    if (payment != null) await ReceivedPayment(invoice, payment);
                }
                else
                {
                    await UpdatePaymentStates(invoice.Id);
                }
            }
        }

        private async Task<InvoiceEntity> ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
        {
            // We want the invoice watcher to look at our invoice after we bumped the payment method fee, so fireEvent=false.
            invoice = await UpdatePaymentStates(invoice.Id, fireEvents: false);
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

        async Task<InvoiceEntity> UpdatePaymentStates(string invoiceId, bool fireEvents = true)
        {
            var network = networks.GetNetwork<BTCPayNetwork>("LTC");
            var invoice = await invoiceRepository.GetInvoice(invoiceId);
            if (invoice == null) return null;
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            var handler = (BitcoinLikePaymentHandler)handlers[pmi];
            var strategy = handlers.GetDerivationStrategy(invoice, network);
            var updatedPaymentEntities = new List<PaymentEntity>();
            var height = await GetHeight();

            foreach (var payment in invoice.GetPayments(false).Where(p => p.PaymentMethodId == pmi))
            {
                var paymentData = handler.ParsePaymentDetails(payment.Details);
                var utxo = _scanners[strategy].Utxos[paymentData.Outpoint.Hash.ToString()];
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
                payment.Status = NBXplorerListener.IsSettled(invoice, paymentData) ? PaymentStatus.Settled : PaymentStatus.Processing;
                updated |= prevStatus != payment.Status;

                // if needed add invoice back to pending to track number of confirmations
                if (paymentData.ConfirmationCount < network.MaxTrackedConfirmation)
                    await invoiceRepository.AddPendingInvoiceIfNotPresent(invoice.Id);
                if (updated) updatedPaymentEntities.Add(payment);
            }

            await paymentService.UpdatePayments(updatedPaymentEntities);
            if (fireEvents && updatedPaymentEntities.Count != 0)
                eventAggregator.Publish(new InvoiceNeedUpdateEvent(invoice.Id));
            return invoice;
        }
    }
}
