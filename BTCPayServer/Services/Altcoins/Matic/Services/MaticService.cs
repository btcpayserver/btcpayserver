#if ALTCOINS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Services.Altcoins.Matic.Payments;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Altcoins.Matic.Configuration;
using BTCPayServer.Services.Altcoins.Matic.UI;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace BTCPayServer.Services.Altcoins.Matic.Services
{
    public class MaticService : EventHostedServiceBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly EventAggregator _eventAggregator;
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly SettingsRepository _settingsRepository;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly IConfiguration _configuration;
        private readonly Dictionary<int, MaticWatcher> _chainHostedServices = new Dictionary<int, MaticWatcher>();

        private readonly Dictionary<int, CancellationTokenSource> _chainHostedServiceCancellationTokenSources =
            new Dictionary<int, CancellationTokenSource>();

        public MaticService(
            IHttpClientFactory httpClientFactory,
            EventAggregator eventAggregator,
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            SettingsRepository settingsRepository,
            InvoiceRepository invoiceRepository,
            IConfiguration configuration) : base(
            eventAggregator)
        {
            _httpClientFactory = httpClientFactory;
            _eventAggregator = eventAggregator;
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _settingsRepository = settingsRepository;
            _invoiceRepository = invoiceRepository;
            _configuration = configuration;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            var chainIds = _btcPayNetworkProvider.GetAll().OfType<MaticBTCPayNetwork>()
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
            var chainIds = _btcPayNetworkProvider.GetAll().OfType<MaticBTCPayNetwork>()
                .Select(network => network.ChainId).Distinct().ToList();
            foreach (var chainId in chainIds)
            {
                try
                {
                    var settings = await _settingsRepository.GetSettingAsync<MaticLikeConfiguration>(
                        MaticLikeConfiguration.SettingsKey(chainId));
                    if (settings is null || string.IsNullOrEmpty(settings.Web3ProviderUrl))
                    {
                        var val = _configuration.GetValue<string>($"chain{chainId}_web3", null);
                        var valUser = _configuration.GetValue<string>($"chain{chainId}_web3_user", null);
                        var valPass = _configuration.GetValue<string>($"chain{chainId}_web3_password", null);
                        if (val != null && First)
                        {
                            Logs.PayServer.LogInformation($"Setting eth chain {chainId} web3 to {val}");
                            settings ??= new MaticLikeConfiguration()
                            {
                                ChainId = chainId,
                                Web3ProviderUrl = val,
                                Web3ProviderPassword = valPass,
                                Web3ProviderUsername = valUser
                            };
                            await _settingsRepository.UpdateSetting(settings,
                                MaticLikeConfiguration.SettingsKey(chainId));
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

            Subscribe<ReserveMaticAddress>();
            Subscribe<SettingsChanged<MaticLikeConfiguration>>();
            Subscribe<CheckWatchers>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is ReserveMaticAddress reserveMaticAddress)
            {
                await HandleReserveNextAddress(reserveMaticAddress);
            }

            if (evt is SettingsChanged<MaticLikeConfiguration> settingsChangedEthConfig)
            {
                await HandleChainWatcher(settingsChangedEthConfig.Settings, cancellationToken);
            }

            if (evt is CheckWatchers)
            {
                await LoopThroughChainWatchers(cancellationToken);
            }

            await base.ProcessEvent(evt, cancellationToken);
        }

        private async Task HandleChainWatcher(MaticLikeConfiguration maticLikeConfiguration,
            CancellationToken cancellationToken)
        {
            if (maticLikeConfiguration is null)
            {
                return;
            }

            if (_chainHostedServiceCancellationTokenSources.ContainsKey(maticLikeConfiguration.ChainId))
            {
                _chainHostedServiceCancellationTokenSources[maticLikeConfiguration.ChainId].Cancel();
                _chainHostedServiceCancellationTokenSources.Remove(maticLikeConfiguration.ChainId);
            }

            if (_chainHostedServices.ContainsKey(maticLikeConfiguration.ChainId))
            {
                await _chainHostedServices[maticLikeConfiguration.ChainId].StopAsync(cancellationToken);
                _chainHostedServices.Remove(maticLikeConfiguration.ChainId);
            }

            if (!string.IsNullOrWhiteSpace(maticLikeConfiguration.Web3ProviderUrl))
            {
                var cts = new CancellationTokenSource();
                _chainHostedServiceCancellationTokenSources.AddOrReplace(maticLikeConfiguration.ChainId, cts);
                _chainHostedServices.AddOrReplace(maticLikeConfiguration.ChainId,
                    new MaticWatcher(maticLikeConfiguration.ChainId, maticLikeConfiguration,
                        _btcPayNetworkProvider, _eventAggregator, _invoiceRepository));
                await _chainHostedServices[maticLikeConfiguration.ChainId].StartAsync(CancellationTokenSource
                    .CreateLinkedTokenSource(cancellationToken, cts.Token).Token);
            }
        }

        private async Task HandleReserveNextAddress(ReserveMaticAddress reserveMaticAddress)
        {
            var store = await _storeRepository.FindStore(reserveMaticAddress.StoreId);
            var maticSupportedPaymentMethod = store.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<MaticSupportedPaymentMethod>()
                .SingleOrDefault(method => method.PaymentId.CryptoCode == reserveMaticAddress.CryptoCode);
            if (maticSupportedPaymentMethod == null)
            {
                _eventAggregator.Publish(new ReserveMaticAddressResponse()
                {
                    OpId = reserveMaticAddress.OpId, Failed = true
                });
                return;
            }

            maticSupportedPaymentMethod.CurrentIndex++;
            var address = maticSupportedPaymentMethod.GetWalletDerivator()?
                .Invoke((int)maticSupportedPaymentMethod.CurrentIndex);

            if (string.IsNullOrEmpty(address))
            {
                _eventAggregator.Publish(new ReserveMaticAddressResponse()
                {
                    OpId = reserveMaticAddress.OpId, Failed = true
                });
                return;
            }
            store.SetSupportedPaymentMethod(maticSupportedPaymentMethod.PaymentId,
                maticSupportedPaymentMethod);
            await _storeRepository.UpdateStore(store);
            _eventAggregator.Publish(new ReserveMaticAddressResponse()
            {
                Address = address,
                Index = maticSupportedPaymentMethod.CurrentIndex,
                CryptoCode = maticSupportedPaymentMethod.CryptoCode,
                OpId = reserveMaticAddress.OpId,
                StoreId = reserveMaticAddress.StoreId,
                XPub = maticSupportedPaymentMethod.XPub
            });
        }

        public async Task<ReserveMaticAddressResponse> ReserveNextAddress(ReserveMaticAddress address)
        {
            address.OpId = string.IsNullOrEmpty(address.OpId) ? Guid.NewGuid().ToString() : address.OpId;
            var tcs = new TaskCompletionSource<ReserveMaticAddressResponse>();
            var subscription = _eventAggregator.Subscribe<ReserveMaticAddressResponse>(response =>
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

        public class ReserveMaticAddressResponse
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

        public class ReserveMaticAddress
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
            return _btcPayNetworkProvider.GetAll().OfType<MaticBTCPayNetwork>()
                .All(network => IsAvailable(network.CryptoCode, out _));
        }

        public bool IsAvailable(string networkCryptoCode, out string error)
        {
            error = null;
            var chainId = _btcPayNetworkProvider.GetNetwork<MaticBTCPayNetwork>(networkCryptoCode)?.ChainId;
            if (chainId != null && _chainHostedServices.TryGetValue(chainId.Value, out var watcher))
            {
                error = watcher.GlobalError;
                return string.IsNullOrEmpty(watcher.GlobalError);
            }
            return false;
        }
    }
}
#endif
