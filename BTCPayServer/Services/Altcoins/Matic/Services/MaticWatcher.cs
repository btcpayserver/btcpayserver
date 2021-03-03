#if ALTCOINS
using Common.Logging;
using Common.Logging.Simple;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Specialized;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Services.Altcoins.Matic.Configuration;
using BTCPayServer.Services.Altcoins.Matic.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;
using NBitcoin.Logging;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.StandardTokenEIP20.ContractDefinition;
using Nethereum.Web3;

namespace BTCPayServer.Services.Altcoins.Matic.Services
{
    public class MaticWatcher : EventHostedServiceBase
    {
        private readonly EventAggregator _eventAggregator;
        private readonly InvoiceRepository _invoiceRepository;
        private int ChainId { get; }
        private readonly HashSet<PaymentMethodId> PaymentMethods;

        private readonly Web3 Web3;
        private readonly List<MaticBTCPayNetwork> Networks;
        public string GlobalError { get; private set; } = "The chain watcher is still starting.";

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            Logs.NodeServer.LogInformation($"Starting MaticWatcher for chain {ChainId}");
            var result = await Web3.Eth.ChainId.SendRequestAsync();
            if (result.Value != ChainId)
            {
                GlobalError =
                    $"The web3 client is connected to a different chain id. Expected {ChainId} but Web3 returned {result.Value}";
                return;
            }

            await base.StartAsync(cancellationToken);
            _eventAggregator.Publish(new CatchUp());
            GlobalError = null;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<MaticService.ReserveMaticAddressResponse>();
            Subscribe<MaticAddressBalanceFetched>();
            Subscribe<CatchUp>();
            base.SubscribeToEvents();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is CatchUp)
            {
                DateTimeOffset start = DateTimeOffset.Now;
                await UpdateAnyPendingEthLikePaymentAndAddressWatchList(cancellationToken);

                TimeSpan diff = start - DateTimeOffset.Now;
                if (diff.TotalSeconds < 5)
                {
                    _ = Task.Delay(TimeSpan.FromSeconds(5 - diff.TotalSeconds), cancellationToken).ContinueWith(task =>
                    {
                        _eventAggregator.Publish(new CatchUp());
                        return Task.CompletedTask;
                    }, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);
                }
            }

            if (evt is MaticAddressBalanceFetched response)
            {
                if (response.ChainId != ChainId)
                {
                    return;
                }

                var network = Networks.SingleOrDefault(payNetwork =>
                    payNetwork.CryptoCode.Equals(response.CryptoCode, StringComparison.InvariantCultureIgnoreCase));

                if (network is null)
                {
                    return;
                }

                var invoice = response.InvoiceEntity;
                if (invoice is null)
                {
                    return;
                }

                var existingPayment = response.MatchedExistingPayment;

                if (existingPayment is null && response.Amount > 0)
                {
                    //new payment
                    var paymentData = new MaticLikePaymentData()
                    {
                        Address = response.Address,
                        CryptoCode = response.CryptoCode,
                        Amount = response.Amount,
                        Network = network,
                        BlockNumber =
                            response.BlockParameter.ParameterType == BlockParameter.BlockParameterType.blockNumber
                                ? (long?)response.BlockParameter.BlockNumber.Value
                                : (long?)null,
                        ConfirmationCount = 0,
                        AccountIndex = response.PaymentMethodDetails.Index,
                        XPub = response.PaymentMethodDetails.XPub
                    };
                    var payment = await _invoiceRepository.AddPayment(invoice.Id, DateTimeOffset.UtcNow,
                        paymentData, network, true);
                    if (payment != null) ReceivedPayment(invoice, payment);
                }
                else if (existingPayment != null)
                {
                    var cd = (MaticLikePaymentData)existingPayment.GetCryptoPaymentData();
                    //existing payment amount was changed. Set to unaccounted and register as a new payment.
                    if (response.Amount == 0 || response.Amount != cd.Amount)
                    {
                        existingPayment.Accounted = false;

                        await _invoiceRepository.UpdatePayments(new List<PaymentEntity>() {existingPayment});
                        if (response.Amount > 0)
                        {
                            var paymentData = new MaticLikePaymentData()
                            {
                                Address = response.Address,
                                CryptoCode = response.CryptoCode,
                                Amount = response.Amount,
                                Network = network,
                                BlockNumber =
                                    response.BlockParameter.ParameterType ==
                                    BlockParameter.BlockParameterType.blockNumber
                                        ? (long?)response.BlockParameter.BlockNumber.Value
                                        : null,
                                ConfirmationCount =
                                    response.BlockParameter.ParameterType ==
                                    BlockParameter.BlockParameterType.blockNumber
                                        ? 1
                                        : 0,
                                
                                AccountIndex = cd.AccountIndex,
                                XPub = cd.XPub
                            };
                            var payment = await _invoiceRepository.AddPayment(invoice.Id, DateTimeOffset.UtcNow,
                                paymentData, network, true);
                            if (payment != null) ReceivedPayment(invoice, payment);
                        }
                    }
                    else if (response.Amount == cd.Amount)
                    {
                        //transition from pending to 1 confirmed
                        if (cd.BlockNumber is null && response.BlockParameter.ParameterType ==
                            BlockParameter.BlockParameterType.blockNumber)
                        {
                            cd.ConfirmationCount = 1;
                            cd.BlockNumber = (long?)response.BlockParameter.BlockNumber.Value;

                            existingPayment.SetCryptoPaymentData(cd);
                            await _invoiceRepository.UpdatePayments(new List<PaymentEntity>() {existingPayment});

                            _eventAggregator.Publish(new Events.InvoiceNeedUpdateEvent(invoice.Id));
                        }
                        //increment confirm count
                        else if (response.BlockParameter.ParameterType ==
                                 BlockParameter.BlockParameterType.blockNumber)
                        {
                            if (response.BlockParameter.BlockNumber.Value > cd.BlockNumber.Value)
                            {
                                cd.ConfirmationCount =
                                    (long)(response.BlockParameter.BlockNumber.Value - cd.BlockNumber.Value);
                            }
                            else
                            {
                                cd.BlockNumber = (long?)response.BlockParameter.BlockNumber.Value;
                                cd.ConfirmationCount = 1;
                            }

                            existingPayment.SetCryptoPaymentData(cd);
                            await _invoiceRepository.UpdatePayments(new List<PaymentEntity>() {existingPayment});

                            _eventAggregator.Publish(new Events.InvoiceNeedUpdateEvent(invoice.Id));
                        }
                    }
                }
            }
        }

        class CatchUp
        {
            public override string ToString()
            {
                return "";
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            Logs.NodeServer.LogInformation($"Stopping MaticWatcher for chain {ChainId}");
            return base.StopAsync(cancellationToken);
        }


        private async Task UpdateAnyPendingEthLikePaymentAndAddressWatchList(CancellationToken cancellationToken)
        {
            var invoiceIds = await _invoiceRepository.GetPendingInvoices();
            if (!invoiceIds.Any())
            {
                return;
            }

            var invoices = await _invoiceRepository.GetInvoices(new InvoiceQuery() {InvoiceId = invoiceIds});
            invoices = invoices
                .Where(entity => PaymentMethods.Any(id => entity.GetPaymentMethod(id) != null))
                .ToArray();

            await UpdatePaymentStates(invoices, cancellationToken);
        }

        private long? LastBlock = null;

        private async Task UpdatePaymentStates(InvoiceEntity[] invoices, CancellationToken cancellationToken)
        {
            if (!invoices.Any())
            {
                return;
            }

            var currentBlock = await Web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();

            foreach (var network in Networks)
            {
                var erc20Network = network as ERC20MaticBTCPayNetwork;
                var paymentMethodId = new PaymentMethodId(network.CryptoCode, MaticPaymentType.Instance);
                var expandedInvoices = invoices
                    .Select(entity => (
                        Invoice: entity,
                        PaymentMethodDetails: entity.GetPaymentMethods().TryGet(paymentMethodId),
                        ExistingPayments: entity.GetPayments(network).Select(paymentEntity => (Payment: paymentEntity,
                            PaymentData: (MaticLikePaymentData)paymentEntity.GetCryptoPaymentData(),
                            Invoice: entity))
                    )).Where(tuple => tuple.PaymentMethodDetails != null).ToList();

                var existingPaymentData = expandedInvoices.SelectMany(tuple =>
                    tuple.ExistingPayments.Where(valueTuple => valueTuple.Payment.Accounted)).ToList();

                var noAccountedPaymentInvoices = expandedInvoices.Where(tuple =>
                    tuple.ExistingPayments.All(valueTuple => !valueTuple.Payment.Accounted)).ToList();

                var tasks = new List<Task>();
                if (existingPaymentData.Any() && currentBlock.Value != LastBlock)
                {
                    Logs.NodeServer.LogInformation(
                        $"Checking {existingPaymentData.Count} existing payments on {expandedInvoices.Count} invoices on {network.CryptoCode}");
                    var blockParameter = new BlockParameter(currentBlock);

                    tasks.Add(Task.WhenAll(existingPaymentData.Select(async tuple =>
                    {
                        var bal = await GetBalance(network, blockParameter, tuple.PaymentData.Address);
                        _eventAggregator.Publish(new MaticAddressBalanceFetched()
                        {
                            Address = tuple.PaymentData.Address,
                            CryptoCode = network.CryptoCode,
                            Amount = bal,
                            MatchedExistingPayment = tuple.Payment,
                            BlockParameter = blockParameter,
                            ChainId = ChainId,
                            InvoiceEntity = tuple.Invoice,
                        });
                    })).ContinueWith(task =>
                    {
                        LastBlock = (long?)currentBlock.Value;
                    }, TaskScheduler.Current));
                }

                if (noAccountedPaymentInvoices.Any())
                {
                    Logs.NodeServer.LogInformation(
                        $"Checking {noAccountedPaymentInvoices.Count} addresses for new payments on {network.CryptoCode}");
                    var blockParameter = BlockParameter.CreatePending();
                    tasks.AddRange(noAccountedPaymentInvoices.Select(async tuple =>
                    {
                        var bal = await GetBalance(network, blockParameter,
                            tuple.PaymentMethodDetails.GetPaymentMethodDetails().GetPaymentDestination());
                        _eventAggregator.Publish(new MaticAddressBalanceFetched()
                        {
                            Address = tuple.PaymentMethodDetails.GetPaymentMethodDetails().GetPaymentDestination(),
                            CryptoCode = network.CryptoCode,
                            Amount = bal,
                            MatchedExistingPayment = null,
                            BlockParameter = blockParameter,
                            ChainId = ChainId,
                            InvoiceEntity = tuple.Invoice,
                            PaymentMethodDetails = (MaticLikeOnChainPaymentMethodDetails) tuple.PaymentMethodDetails.GetPaymentMethodDetails()
                        });
                    }));
                }

                await Task.WhenAll(tasks);
            }
        }

        public class MaticAddressBalanceFetched
        {
            public BlockParameter BlockParameter { get; set; }
            public int ChainId { get; set; }
            public string Address { get; set; }
            public string CryptoCode { get; set; }
            public long Amount { get; set; }
            public InvoiceEntity InvoiceEntity { get; set; }
            public PaymentEntity MatchedExistingPayment { get; set; }
            public MaticLikeOnChainPaymentMethodDetails PaymentMethodDetails { get; set; }

            public override string ToString()
            {
                return "";            
            }
        }

        private void ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
        {
            _eventAggregator.Publish(
                new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) {Payment = payment});
        }

        private async Task<long> GetBalance(MaticBTCPayNetwork network, BlockParameter blockParameter, string address)
        {
            if (network is ERC20MaticBTCPayNetwork erc20BTCPayNetwork)
            {
                return (long)(await Web3.Eth.GetContractHandler(erc20BTCPayNetwork.SmartContractAddress)
                    .QueryAsync<BalanceOfFunction, BigInteger>(new BalanceOfFunction() {Owner = address}));
            }
            else
            {
                return (long)(await Web3.Eth.GetBalance.SendRequestAsync(address, blockParameter)).Value;
            }
        }

        public MaticWatcher(int chainId, MaticLikeConfiguration config,
            BTCPayNetworkProvider btcPayNetworkProvider,
            EventAggregator eventAggregator, InvoiceRepository invoiceRepository) :
            base(eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _invoiceRepository = invoiceRepository;
            ChainId = chainId;
            AuthenticationHeaderValue headerValue = null;
            if (!string.IsNullOrEmpty(config.Web3ProviderUsername))
            {
                var val = config.Web3ProviderUsername;
                if (!string.IsNullOrEmpty(config.Web3ProviderUsername))
                {
                    val += $":{config.Web3ProviderUsername}";
                }
                
                headerValue = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(
                        System.Text.Encoding.ASCII.GetBytes(val)));
            }
            var debugLoggerFactoryAdapter = new DebugLoggerFactoryAdapter();
            LogManager.Adapter = debugLoggerFactoryAdapter;
          
            var iLog = LogManager.GetLogger<ILog>();
            Web3 = new Web3(config.Web3ProviderUrl, iLog, headerValue);
            Networks = btcPayNetworkProvider.GetAll()
                .OfType<MaticBTCPayNetwork>()
                .Where(network => network.ChainId == chainId)
                .ToList();
            PaymentMethods = Networks
                .Select(network => new PaymentMethodId(network.CryptoCode, MaticPaymentType.Instance))
                .ToHashSet();
        }
    }
}
#endif
