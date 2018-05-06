using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBXplorer;

namespace BTCPayServer.Payments.Ethereum
{
    public class Web3Listener : IHostedService
    {
        EventAggregator _Aggregator;
        IApplicationLifetime _Lifetime;
        InvoiceRepository _InvoiceRepository;
        private readonly Web3Provider _web3Provider;
        private TaskCompletionSource<bool> _RunningTask;
        private CancellationTokenSource _Cts;
        BTCPayNetworkProvider _NetworkProvider;
        CompositeDisposable leases = new CompositeDisposable();
        private Timer _ListenPoller;


        TimeSpan _PollInterval;
        public TimeSpan PollInterval
        {
            get
            {
                return _PollInterval;
            }
            set
            {
                _PollInterval = value;
                if (_ListenPoller != null)
                {
                    _ListenPoller.Change(0, (int)value.TotalMilliseconds);
                }
            }
        }


        public Web3Listener(
            InvoiceRepository invoiceRepository,
            Web3Provider web3Provider,
            BTCPayNetworkProvider networkProvider,
            EventAggregator aggregator, IApplicationLifetime lifetime)
        {
            PollInterval = TimeSpan.FromMinutes(1.0);
            _InvoiceRepository = invoiceRepository;
            _web3Provider = web3Provider;
            _Aggregator = aggregator;
            _Lifetime = lifetime;
            _NetworkProvider = networkProvider;
        }


        public Task StartAsync(CancellationToken cancellationToken)
        {
            _RunningTask = new TaskCompletionSource<bool>();
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _ListenPoller = new Timer(async s =>
            {
               
            }, null, 0, (int)PollInterval.TotalMilliseconds);
            leases.Add(_ListenPoller);
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

    }
}
