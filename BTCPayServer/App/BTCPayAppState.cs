#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BTCPayApp.CommonServer;
using BTCPayServer.Events;
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
    private readonly IHubContext<BTCPayAppHub, IBTCPayAppHubClient> _hubContext;
    private readonly ILogger<BTCPayAppState> _logger;
    private readonly ExplorerClientProvider _explorerClientProvider;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly EventAggregator _eventAggregator;
    private readonly HubLifetimeManager<BTCPayAppHub> _lifetimeManager;
    private CompositeDisposable? _compositeDisposable;
    public ExplorerClient ExplorerClient { get; private set; }
    private DerivationSchemeParser _derivationSchemeParser;
    // private readonly ConcurrentDictionary<string, TrackedSource> _connectionScheme = new();

    public BTCPayAppState(
        IHubContext<BTCPayAppHub, IBTCPayAppHubClient> hubContext,
        ILogger<BTCPayAppState> logger,
        ExplorerClientProvider explorerClientProvider,
        BTCPayNetworkProvider networkProvider,
        EventAggregator eventAggregator,
        HubLifetimeManager<BTCPayAppHub> lifetimeManager)
    {
        _hubContext = hubContext;
        _logger = logger;
        _explorerClientProvider = explorerClientProvider;
        _networkProvider = networkProvider;
        _eventAggregator = eventAggregator;
        _lifetimeManager = lifetimeManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ExplorerClient = _explorerClientProvider.GetExplorerClient("BTC");
        _derivationSchemeParser = new DerivationSchemeParser(_networkProvider.BTC);
        _compositeDisposable = new();
        _compositeDisposable.Add(
            _eventAggregator.Subscribe<NewBlockEvent>(OnNewBlock));
        _compositeDisposable.Add(
            _eventAggregator.Subscribe<NewOnChainTransactionEvent>(OnNewTransaction));
        return Task.CompletedTask;
    }

    private void OnNewTransaction(NewOnChainTransactionEvent obj)
    {
        if (obj.CryptoCode != "BTC")
            return;

        var identifier = obj.NewTransactionEvent.TrackedSource.ToString()!;
        _hubContext.Clients
            .Group(identifier)
            .TransactionDetected(identifier, obj.NewTransactionEvent.TransactionData.TransactionHash.ToString());
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

    public async Task<AppHandshakeResponse> Handshake(string contextConnectionId, AppHandshake handshake)
    {
        foreach (var ts in handshake.Identifiers)
        {
            try
            {
                await _hubContext.Groups.AddToGroupAsync(contextConnectionId, ts);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error during handshake");
                throw;
            }
        }

        //TODO: Check if the provided identifiers are already tracked on the server
        //TODO: Maybe also introduce a checkpoint to make sure nothing is missed, but this may be somethign to handle alongside VSS
        return new AppHandshakeResponse() {IdentifiersAcknowledged = handshake.Identifiers};
    }

    public async Task<Dictionary<string, string>> Pair(string contextConnectionId, PairRequest request)
    {
        var result = new Dictionary<string, string>();
        foreach (var derivation in request.Derivations)
        {
            if (derivation.Value is null)
            {
                var id = await ExplorerClient.CreateGroupAsync();

                result.Add(derivation.Key, id.TrackedSource);
            }
            else
            {
                var strategy = _derivationSchemeParser.ParseOutputDescriptor(derivation.Value);
                result.Add(derivation.Key, TrackedSource.Create(strategy.Item1).ToString());
            }
        }

        await Handshake(contextConnectionId, new AppHandshake() {Identifiers = result.Values.ToArray()});
        return result;
    }

    public readonly ConcurrentDictionary<string, string> GroupToConnectionId = new();


    public async Task MasterNodePong(string group, string contextConnectionId, bool active)
    {
        if (active)
        {
            GroupToConnectionId.AddOrUpdate(group, contextConnectionId, (a, b) => contextConnectionId);
        }
        else if (GroupToConnectionId.TryGetValue(group, out var connId) && connId == contextConnectionId)
        {
            GroupToConnectionId.TryRemove(group, out _);
        }
    }

    public async Task Disconnected(string contextConnectionId)
    {
        foreach (var group in GroupToConnectionId.Where(a => a.Value == contextConnectionId).Select(a => a.Key)
                     .ToArray())
        {
            GroupToConnectionId.TryRemove(group, out _);
        }
    }

    public async Task Connected(string contextConnectionId)
    {
    }
}
