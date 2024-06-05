#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom.Events;
using BTCPayApp.CommonServer;
using BTCPayServer.Events;
using BTCPayServer.Lightning;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;
using LightningPayment = BTCPayApp.CommonServer.Models.LightningPayment;
using NewBlockEvent = BTCPayServer.Events.NewBlockEvent;

namespace BTCPayServer.Controllers;

public class BTCPayAppState : IHostedService
{
    private readonly IHubContext<BTCPayAppHub, IBTCPayAppHubClient> _hubContext;
    private readonly ILogger<BTCPayAppState> _logger;
    private readonly ExplorerClientProvider _explorerClientProvider;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly EventAggregator _eventAggregator;
    private readonly IServiceProvider _serviceProvider;
    private CompositeDisposable? _compositeDisposable;
    public ExplorerClient ExplorerClient { get; private set; }
    
    private DerivationSchemeParser _derivationSchemeParser;
    // private readonly ConcurrentDictionary<string, TrackedSource> _connectionScheme = new();

    public event EventHandler<(string,LightningPayment)>? OnPaymentUpdate;
    public BTCPayAppState(
        IHubContext<BTCPayAppHub, IBTCPayAppHubClient> hubContext,
        ILogger<BTCPayAppState> logger,
        ExplorerClientProvider explorerClientProvider,
        BTCPayNetworkProvider networkProvider,
        EventAggregator eventAggregator, IServiceProvider serviceProvider)
    {
        _hubContext = hubContext;
        _logger = logger;
        _explorerClientProvider = explorerClientProvider;
        _networkProvider = networkProvider;
        _eventAggregator = eventAggregator;
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts ??= new CancellationTokenSource();
        ExplorerClient = _explorerClientProvider.GetExplorerClient("BTC");
        _derivationSchemeParser = new DerivationSchemeParser(_networkProvider.BTC);
        _compositeDisposable = new();
        _compositeDisposable.Add(
            _eventAggregator.Subscribe<NewBlockEvent>(OnNewBlock));
        _compositeDisposable.Add(
            _eventAggregator.SubscribeAsync<NewOnChainTransactionEvent>(OnNewTransaction));
        _ = UpdateNodeInfo();
        return Task.CompletedTask;
    }
    
    private string _nodeInfo = string.Empty;
    private async Task UpdateNodeInfo()
    {
        while (!_cts.Token.IsCancellationRequested)
        {


            try
            {
                var res = await _serviceProvider.GetRequiredService<PaymentMethodHandlerDictionary>().GetLightningHandler(ExplorerClient.CryptoCode).GetNodeInfo(
                    new LightningPaymentMethodConfig() {InternalNodeRef = LightningPaymentMethodConfig.InternalNode},
                    null,
                    false, false);
                if (res.Any())
                {
                    var newInf = res.First();
                    if (_networkProvider.NetworkType == ChainName.Regtest)
                    {
                        newInf = new NodeInfo(newInf.NodeId, "127.0.0.1", 30893);
                    }
                    if (newInf.ToString() != _nodeInfo)
                    {
                        _nodeInfo = newInf.ToString();
                        await _hubContext.Clients.All.NotifyServerNode(_nodeInfo);
                    }

                }
                
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error during node info update");
               
            }
            await Task.Delay(TimeSpan.FromMinutes(string.IsNullOrEmpty(_nodeInfo)? 1:5), _cts.Token);
        }

    }

    private async  Task OnNewTransaction(NewOnChainTransactionEvent obj)
    {
        if (obj.CryptoCode != "BTC")
            return;

        var identifier = obj.NewTransactionEvent.TrackedSource.ToString()!;
        var explorer = _explorerClientProvider.GetExplorerClient(obj.CryptoCode);
        var expandedTx = await explorer.GetTransactionAsync(obj.NewTransactionEvent.TrackedSource,
            obj.NewTransactionEvent.TransactionData.TransactionHash);

        await _hubContext.Clients
            .Group(identifier)
            .TransactionDetected(new TransactionDetectedRequest()
            {
                SpentScripts = expandedTx.Inputs.Select(input => input.ScriptPubKey.ToHex()).ToArray(),
                ReceivedScripts = expandedTx.Outputs.Select(output => output.ScriptPubKey.ToHex()).ToArray(),
                TxId = obj.NewTransactionEvent.TransactionData.TransactionHash.ToString(),
                Confirmed = obj.NewTransactionEvent.BlockId is not null &&
                            obj.NewTransactionEvent.BlockId != uint256.Zero,
                Identifier = identifier
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

    public async Task<AppHandshakeResponse> Handshake(string contextConnectionId, AppHandshake handshake)
    {
        foreach (var ts in handshake.Identifiers)
        {
            try
            {
                if (TrackedSource.TryParse(ts, out var trackedSource, ExplorerClient.Network))
                {
                    if (trackedSource is GroupTrackedSource groupTrackedSource)
                    {
                        
                    }
                    
                    ExplorerClient.Track(trackedSource);
                }
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
    private CancellationTokenSource _cts;


    public async Task<bool> IdentifierActive(string group, string contextConnectionId, bool active)
    {
        if (active)
        {
            return GroupToConnectionId.TryAdd(group, contextConnectionId);
        }
        else if (GroupToConnectionId.TryGetValue(group, out var connId) && connId == contextConnectionId)
        {
            return GroupToConnectionId.TryRemove(group, out _);
        }
        return false;
    }

    public async Task Disconnected(string contextConnectionId)
    {
        foreach (var group in GroupToConnectionId.Where(a => a.Value == contextConnectionId).Select(a => a.Key)
                     .ToArray())
        {
            if (GroupToConnectionId.TryRemove(group, out _))
            {
                GroupRemoved?.Invoke(this, group);
            }

        }
    }

    public event EventHandler<string>? GroupRemoved;
    
    public async Task Connected(string contextConnectionId)
    {
        if(_nodeInfo.Length > 0)
            await _hubContext.Clients.Client(contextConnectionId).NotifyServerNode(_nodeInfo);
    }

    public async Task PaymentUpdate(string identifier, LightningPayment lightningPayment)
    {
        _logger.LogInformation("Payment update for {identifier} {}", identifier);
        OnPaymentUpdate?.Invoke(this, (identifier, lightningPayment));
    }
    
    
}
