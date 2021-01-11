#if ALTCOINS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Services.Altcoins.Ethereum.Payments;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Altcoins.Ethereum.Configuration;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace BTCPayServer.Services.Altcoins.Ethereum.Services
{
    public class EthereumService : EventHostedServiceBase
    {
        private readonly EventAggregator _eventAggregator;
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly SettingsRepository _settingsRepository;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly IConfiguration _configuration;
        private readonly Dictionary<int, EthereumWatcher> _chainHostedServices = new Dictionary<int, EthereumWatcher>();

        private readonly Dictionary<int, CancellationTokenSource> _chainHostedServiceCancellationTokenSources =
            new Dictionary<int, CancellationTokenSource>();

        public EthereumService(
            EventAggregator eventAggregator,
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            SettingsRepository settingsRepository,
            InvoiceRepository invoiceRepository,
            IConfiguration configuration) : base(
            eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _settingsRepository = settingsRepository;
            _invoiceRepository = invoiceRepository;
            _configuration = configuration;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            var chainIds = _btcPayNetworkProvider.GetAll().OfType<EthereumBTCPayNetwork>()
                .Select(network => network.ChainId).Distinct().ToList();
            if (!chainIds.Any())
            {
                return;
            }

            await base.StartAsync(cancellationToken);
            _ = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    _eventAggregator.Publish(new CheckWatchers());
                    await Task.Delay(IsAllAvailable() ? TimeSpan.FromDays(1) : TimeSpan.FromSeconds(5),
                        cancellationToken);
                }
            }, cancellationToken);
        }

        private static bool First = true;

        private async Task LoopThroughChainWatchers(CancellationToken cancellationToken)
        {
            var chainIds = _btcPayNetworkProvider.GetAll().OfType<EthereumBTCPayNetwork>()
                .Select(network => network.ChainId).Distinct().ToList();
            foreach (var chainId in chainIds)
            {
                try
                {
                    var settings = await _settingsRepository.GetSettingAsync<EthereumLikeConfiguration>(
                        EthereumLikeConfiguration.SettingsKey(chainId));
                    if (settings is null || string.IsNullOrEmpty(settings.Web3ProviderUrl))
                    {
                        var val = _configuration.GetValue<string>($"chain{chainId}_web3", null);
                        var valUser = _configuration.GetValue<string>($"chain{chainId}_web3_user", null);
                        var valPass = _configuration.GetValue<string>($"chain{chainId}_web3_password", null);
                        if (val != null && First)
                        {
                            Logs.PayServer.LogInformation($"Setting eth chain {chainId} web3 to {val}");
                            settings ??= new EthereumLikeConfiguration()
                            {
                                ChainId = chainId,
                                Web3ProviderUrl = val,
                                Web3ProviderPassword = valPass,
                                Web3ProviderUsername = valUser
                            };
                            await _settingsRepository.UpdateSetting(settings,
                                EthereumLikeConfiguration.SettingsKey(chainId));
                        }
                    }

                    var currentlyRunning = _chainHostedServices.ContainsKey(chainId);
                    if (!currentlyRunning || (currentlyRunning))
                    {
                        await HandleChainWatcher(settings, cancellationToken);
                    }
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            First = false;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var chainHostedService in _chainHostedServices.Values)
            {
                _ = chainHostedService.StopAsync(cancellationToken);
            }

            return base.StopAsync(cancellationToken);
        }

        protected override void SubscribeToEvents()
        {
            base.SubscribeToEvents();

            Subscribe<ReserveEthereumAddress>();
            Subscribe<SettingsChanged<EthereumLikeConfiguration>>();
            Subscribe<CheckWatchers>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is ReserveEthereumAddress reserveEthereumAddress)
            {
                await HandleReserveNextAddress(reserveEthereumAddress);
            }

            if (evt is SettingsChanged<EthereumLikeConfiguration> settingsChangedEthConfig)
            {
                await HandleChainWatcher(settingsChangedEthConfig.Settings, cancellationToken);
            }

            if (evt is CheckWatchers)
            {
                await LoopThroughChainWatchers(cancellationToken);
            }

            await base.ProcessEvent(evt, cancellationToken);
        }

        private async Task HandleChainWatcher(EthereumLikeConfiguration ethereumLikeConfiguration,
            CancellationToken cancellationToken)
        {
            if (ethereumLikeConfiguration is null)
            {
                return;
            }

            if (_chainHostedServiceCancellationTokenSources.ContainsKey(ethereumLikeConfiguration.ChainId))
            {
                _chainHostedServiceCancellationTokenSources[ethereumLikeConfiguration.ChainId].Cancel();
                _chainHostedServiceCancellationTokenSources.Remove(ethereumLikeConfiguration.ChainId);
            }

            if (_chainHostedServices.ContainsKey(ethereumLikeConfiguration.ChainId))
            {
                await _chainHostedServices[ethereumLikeConfiguration.ChainId].StopAsync(cancellationToken);
                _chainHostedServices.Remove(ethereumLikeConfiguration.ChainId);
            }

            if (!string.IsNullOrWhiteSpace(ethereumLikeConfiguration.Web3ProviderUrl))
            {
                var cts = new CancellationTokenSource();
                _chainHostedServiceCancellationTokenSources.AddOrReplace(ethereumLikeConfiguration.ChainId, cts);
                _chainHostedServices.AddOrReplace(ethereumLikeConfiguration.ChainId,
                    new EthereumWatcher(ethereumLikeConfiguration.ChainId, ethereumLikeConfiguration,
                        _btcPayNetworkProvider, _eventAggregator, _invoiceRepository));
                await _chainHostedServices[ethereumLikeConfiguration.ChainId].StartAsync(CancellationTokenSource
                    .CreateLinkedTokenSource(cancellationToken, cts.Token).Token);
            }
        }

        private async Task HandleReserveNextAddress(ReserveEthereumAddress reserveEthereumAddress)
        {
            var store = await _storeRepository.FindStore(reserveEthereumAddress.StoreId);
            var ethereumSupportedPaymentMethod = store.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<EthereumSupportedPaymentMethod>()
                .SingleOrDefault(method => method.PaymentId.CryptoCode == reserveEthereumAddress.CryptoCode);
            if (ethereumSupportedPaymentMethod == null)
            {
                _eventAggregator.Publish(new ReserveEthereumAddressResponse()
                {
                    OpId = reserveEthereumAddress.OpId, Failed = true
                });
                return;
            }

            ethereumSupportedPaymentMethod.CurrentIndex++;
            var address = ethereumSupportedPaymentMethod.GetWalletDerivator()?
                .Invoke((int)ethereumSupportedPaymentMethod.CurrentIndex);

            if (string.IsNullOrEmpty(address))
            {
                _eventAggregator.Publish(new ReserveEthereumAddressResponse()
                {
                    OpId = reserveEthereumAddress.OpId, Failed = true
                });
                return;
            }
            store.SetSupportedPaymentMethod(ethereumSupportedPaymentMethod.PaymentId,
                ethereumSupportedPaymentMethod);
            await _storeRepository.UpdateStore(store);
            _eventAggregator.Publish(new ReserveEthereumAddressResponse()
            {
                Address = address,
                Index = ethereumSupportedPaymentMethod.CurrentIndex,
                CryptoCode = ethereumSupportedPaymentMethod.CryptoCode,
                OpId = reserveEthereumAddress.OpId,
                StoreId = reserveEthereumAddress.StoreId,
                XPub = ethereumSupportedPaymentMethod.XPub
            });
        }

        public async Task<ReserveEthereumAddressResponse> ReserveNextAddress(ReserveEthereumAddress address)
        {
            address.OpId = string.IsNullOrEmpty(address.OpId) ? Guid.NewGuid().ToString() : address.OpId;
            var tcs = new TaskCompletionSource<ReserveEthereumAddressResponse>();
            var subscription = _eventAggregator.Subscribe<ReserveEthereumAddressResponse>(response =>
            {
                if (response.OpId == address.OpId)
                {
                    tcs.SetResult(response);
                }
            });
            _eventAggregator.Publish(address);

            if (tcs.Task.Wait(TimeSpan.FromSeconds(60)))
            {
                subscription?.Dispose();
                return await tcs.Task;
            }

            subscription?.Dispose();
            return null;
        }

        public class CheckWatchers
        {
            public override string ToString()
            {
                return "";
            }
        }

        public class ReserveEthereumAddressResponse
        {
            public string StoreId { get; set; }
            public string CryptoCode { get; set; }
            public string Address { get; set; }
            public long Index { get; set; }
            public string OpId { get; set; }
            public string XPub { get; set; }
            public bool Failed { get; set; }

            public override string ToString()
            {
                return $"Reserved {CryptoCode} address {Address} for store {StoreId}";
            }
        }

        public class ReserveEthereumAddress
        {
            public string StoreId { get; set; }
            public string CryptoCode { get; set; }
            public string OpId { get; set; }

            public override string ToString()
            {
                return $"Reserving {CryptoCode} address for store {StoreId}";
            }
        }

        public bool IsAllAvailable()
        {
            return _btcPayNetworkProvider.GetAll().OfType<EthereumBTCPayNetwork>()
                .All(network => IsAvailable(network.CryptoCode, out _));
        }

        public bool IsAvailable(string networkCryptoCode, out string error)
        {
            error = null;
            var chainId = _btcPayNetworkProvider.GetNetwork<EthereumBTCPayNetwork>(networkCryptoCode)?.ChainId;
            if (chainId != null && _chainHostedServices.TryGetValue(chainId.Value, out var watcher))
            {
                error = watcher.GlobalError;
                return string.IsNullOrEmpty(watcher.GlobalError);
            }
            return false;
        }

        public Web3 GetWeb3(int chainId)
        {
            if (_chainHostedServices.TryGetValue(chainId, out var watcher) && string.IsNullOrEmpty(watcher.GlobalError))
            {
                return watcher.Web3;
            }

            return null;
        } 
        public async Task<ulong?> GetBalance(EthereumBTCPayNetwork network, string address)
        {
            if (_chainHostedServices.TryGetValue(network.ChainId, out var watcher) && string.IsNullOrEmpty(watcher.GlobalError))
            {
                return await watcher.GetBalance(network, BlockParameter.CreateLatest(), address);
            }

            return null;
        }
    }
}
#endif
