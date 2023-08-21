#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayApp.CommonServer;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Data;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;
using NewBlockEvent = BTCPayServer.Events.NewBlockEvent;

namespace BTCPayServer.Controllers;

public class BTCPayAppState : IHostedService
{
    private readonly IHubContext<BTCPayAppHub, IBTCPayAppServerClient> _hubContext;
    private readonly ILogger<BTCPayAppState> _logger;
    private readonly ExplorerClientProvider _explorerClientProvider;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly EventAggregator _eventAggregator;
    private CompositeDisposable? _compositeDisposable;
    public ExplorerClient ExplorerClient { get; private set; }
    private DerivationSchemeParser _derivationSchemeParser;
    private readonly ConcurrentDictionary<string, TrackedSource> _connectionScheme = new();

    public BTCPayAppState(
        IHubContext<BTCPayAppHub, IBTCPayAppServerClient> hubContext,
        ILogger<BTCPayAppState> logger,
        ExplorerClientProvider explorerClientProvider,
        BTCPayNetworkProvider networkProvider,
        EventAggregator eventAggregator)
    {
        _hubContext = hubContext;
        _logger = logger;
        _explorerClientProvider = explorerClientProvider;
        _networkProvider = networkProvider;
        _eventAggregator = eventAggregator;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ExplorerClient = _explorerClientProvider.GetExplorerClient("BTC");
        _derivationSchemeParser = new DerivationSchemeParser(_networkProvider.BTC);
        _compositeDisposable = new();
        _compositeDisposable.Add(
            _eventAggregator.Subscribe<NewBlockEvent>(OnNewBlock));
        _compositeDisposable.Add(
            _eventAggregator.Subscribe<NewTransactionEvent>(OnNewTransaction));
        return Task.CompletedTask;
    }

    private void OnNewTransaction(NewTransactionEvent obj)
    {
        if (obj.CryptoCode != "BTC")
            return;

        _connectionScheme.Where(pair => pair.Value == obj.TrackedSource)
            .Select(pair => pair.Key)
            .ToList()
            .ForEach(connectionId =>
            {
                _hubContext.Clients.Client(connectionId)
                    .TransactionDetected(obj.TransactionData.TransactionHash.ToString());
            });
    }

    private void OnNewBlock(NewBlockEvent obj)
    {
        if (obj.CryptoCode != "BTC")
            return;
        _hubContext.Clients.All.NewBlock(obj.Hash.ToString());
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _compositeDisposable?.Dispose();
        return Task.CompletedTask;
    }

    public TrackedSource? GetConnectionState(string connectionId)
    {
        _connectionScheme.TryGetValue(connectionId, out var res);
        return res;
    }

    public async Task Handshake(string contextConnectionId, AppHandshake handshake)
    {
        try
        {
            var ts =
                TrackedSource.Create(_derivationSchemeParser.Parse(handshake.DerivationScheme));
            await ExplorerClient.TrackAsync(ts);
            _connectionScheme.AddOrReplace(contextConnectionId, ts);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error during handshake");
            throw;
        }
    }

    public void RemoveConnection(string contextConnectionId)
    {
        _connectionScheme.TryRemove(contextConnectionId, out _);
    }
}

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
public class BTCPayAppHub : Hub<IBTCPayAppServerClient>, IBTCPayAppServerHub
{
    private readonly BTCPayAppState _appState;
    private readonly BTCPayWallet _wallet;


    public BTCPayAppHub(BTCPayAppState appState, BTCPayWalletProvider walletProvider)
    {
        _appState = appState;
        _wallet = walletProvider.GetWallet("BTC");
    }


    public override async Task OnConnectedAsync()
    {
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _appState.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task Handshake(AppHandshake handshake)
    {
        return _appState.Handshake(Context.ConnectionId, handshake);
    }

    public async Task GetTransactions()
    {
       var deriv =  _appState.GetConnectionState(Context.ConnectionId);
        if(deriv is null)
            throw new InvalidOperationException("Handshake not done");
        var txs = await _appState.ExplorerClient.GetTransactionsAsync(deriv);
        if (txs is null)
        {
            throw new InvalidOperationException("NBXplorer failed to get transactions");
        }

        

    }
}
