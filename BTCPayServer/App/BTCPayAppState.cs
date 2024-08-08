#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayApp.CommonServer;
using BTCPayServer.Events;
using BTCPayServer.Lightning;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;
using NewBlockEvent = BTCPayServer.Events.NewBlockEvent;

namespace BTCPayServer.Controllers;


public record ConnectedInstance(
    string UserId,
    long? DeviceIdentifier,
    bool Master,
    // string ProvidedAccessKey,
    HashSet<string> Groups);
public class BTCPayAppState : IHostedService
{
    private readonly StoreRepository _storeRepository;
    private readonly IHubContext<BTCPayAppHub, IBTCPayAppHubClient> _hubContext;
    private readonly ILogger<BTCPayAppState> _logger;
    private readonly ExplorerClientProvider _explorerClientProvider;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly EventAggregator _eventAggregator;
    private readonly IServiceProvider _serviceProvider;
    private CompositeDisposable? _compositeDisposable;
    public ExplorerClient ExplorerClient { get; private set; }

    private DerivationSchemeParser _derivationSchemeParser;


    public ConcurrentDictionary<string, ConnectedInstance> Connections { get; set; } = new();
    

    private CancellationTokenSource? _cts;

    public event EventHandler<(string, LightningInvoice)>? OnInvoiceUpdate;
    public event EventHandler<string>? GroupRemoved;

    public BTCPayAppState(
        StoreRepository storeRepository,
        IHubContext<BTCPayAppHub, IBTCPayAppHubClient> hubContext,
        ILogger<BTCPayAppState> logger,
        ExplorerClientProvider explorerClientProvider,
        BTCPayNetworkProvider networkProvider,
        EventAggregator eventAggregator, IServiceProvider serviceProvider)
    {
        _storeRepository = storeRepository;
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
        _compositeDisposable = new CompositeDisposable();
        _compositeDisposable.Add(_eventAggregator.Subscribe<NewBlockEvent>(OnNewBlock));
        _compositeDisposable.Add(_eventAggregator.SubscribeAsync<NewOnChainTransactionEvent>(OnNewTransaction));
        _compositeDisposable.Add(_eventAggregator.SubscribeAsync<UserNotificationsUpdatedEvent>(UserNotificationsUpdatedEvent));
        _compositeDisposable.Add(_eventAggregator.SubscribeAsync<InvoiceEvent>(InvoiceChangedEvent));
        // User events
        _compositeDisposable.Add(_eventAggregator.SubscribeAsync<UserUpdatedEvent>(UserUpdatedEvent));
        _compositeDisposable.Add(_eventAggregator.SubscribeAsync<UserDeletedEvent>(UserDeletedEvent));
        // Store events
        _compositeDisposable.Add(_eventAggregator.SubscribeAsync<StoreCreatedEvent>(StoreCreatedEvent));
        _compositeDisposable.Add(_eventAggregator.SubscribeAsync<StoreUpdatedEvent>(StoreUpdatedEvent));
        _compositeDisposable.Add(_eventAggregator.SubscribeAsync<StoreRemovedEvent>(StoreRemovedEvent));
        _compositeDisposable.Add(_eventAggregator.SubscribeAsync<UserStoreAddedEvent>(StoreUserAddedEvent));
        _compositeDisposable.Add(_eventAggregator.SubscribeAsync<UserStoreUpdatedEvent>(StoreUserUpdatedEvent));
        _compositeDisposable.Add(_eventAggregator.SubscribeAsync<UserStoreRemovedEvent>(StoreUserRemovedEvent));
        _ = UpdateNodeInfo();
        return Task.CompletedTask;
    }

    private async Task UserUpdatedEvent(UserUpdatedEvent arg)
    {
        var ev = new ServerEvent("user-updated") { UserId = arg.User.Id, Detail = arg.Detail };
        await _hubContext.Clients.Group(arg.User.Id).NotifyServerEvent(ev);
    }

    private async Task UserDeletedEvent(UserDeletedEvent arg)
    {
        var ev = new ServerEvent("user-deleted") { UserId = arg.User.Id };
        await _hubContext.Clients.Group(arg.User.Id).NotifyServerEvent(ev);
    }

    private async Task InvoiceChangedEvent(InvoiceEvent arg)
    {
        var ev = new ServerEvent("invoice-updated") { StoreId = arg.Invoice.StoreId, InvoiceId = arg.InvoiceId, Detail = arg.Invoice.Status.ToString() };
        await _hubContext.Clients.Group(arg.Invoice.StoreId).NotifyServerEvent(ev);
    }

    private async Task UserNotificationsUpdatedEvent(UserNotificationsUpdatedEvent arg)
    {
        var ev = new ServerEvent("notifications-updated") { UserId = arg.UserId };
        await _hubContext.Clients.Group(arg.UserId).NotifyServerEvent(ev);
    }

    private async Task StoreCreatedEvent(StoreCreatedEvent arg)
    {
        var ev = new ServerEvent("store-created") { StoreId = arg.StoreId };
        
        foreach (var su in arg.StoreUsers)
        {
            var cIds = Connections.Where(pair => pair.Value.UserId == su.UserId).Select(pair => pair.Key).ToArray();
            await AddToGroup(arg.StoreId, cIds);
        }
        await _hubContext.Clients.Group(arg.StoreId).NotifyServerEvent(ev);
    }

    private async Task StoreUpdatedEvent(StoreUpdatedEvent arg)
    {
        var ev = new ServerEvent("store-updated") { StoreId = arg.StoreId, Detail = arg.Detail };
        await _hubContext.Clients.Group(arg.StoreId).NotifyServerEvent(ev);
    }

    private async Task StoreRemovedEvent(StoreRemovedEvent arg)
    {
        var ev = new ServerEvent("store-removed") { StoreId = arg.StoreId };
        await _hubContext.Clients.Group(arg.StoreId).NotifyServerEvent(ev);
    }

    private async Task StoreUserAddedEvent(UserStoreAddedEvent arg)
    {
        var cIds = Connections.Where(pair => pair.Value.UserId == arg.UserId).Select(pair => pair.Key).ToArray();
        await AddToGroup(arg.StoreId, cIds);
        var ev = new ServerEvent("user-store-added") {StoreId = arg.StoreId, UserId = arg.UserId, Detail = arg.Detail};
        await _hubContext.Clients.Groups(arg.StoreId, arg.UserId).NotifyServerEvent(ev);
    }

    private async Task StoreUserUpdatedEvent(UserStoreUpdatedEvent arg)
    {
        var ev = new ServerEvent("user-store-updated") {StoreId = arg.StoreId, UserId = arg.UserId, Detail = arg.Detail};
        await _hubContext.Clients.Groups(arg.StoreId, arg.UserId).NotifyServerEvent(ev);
    }

    private async Task StoreUserRemovedEvent(UserStoreRemovedEvent arg)
    {
        var ev = new ServerEvent("user-store-removed") {StoreId = arg.StoreId, UserId = arg.UserId};
        await _hubContext.Clients.Groups(arg.StoreId, arg.UserId).NotifyServerEvent(ev);

        await RemoveFromGroup(arg.StoreId, Connections.Where(pair => pair.Value.UserId == arg.UserId).Select(pair => pair.Key).ToArray());
    }

    private string _nodeInfo = string.Empty;

    private async Task UpdateNodeInfo()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var res = await _serviceProvider.GetRequiredService<PaymentMethodHandlerDictionary>()
                    .GetLightningHandler(ExplorerClient.CryptoCode).GetNodeInfo(
                        new LightningPaymentMethodConfig()
                        {
                            InternalNodeRef = LightningPaymentMethodConfig.InternalNode
                        },
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

            await Task.Delay(TimeSpan.FromMinutes(string.IsNullOrEmpty(_nodeInfo) ? 1 : 5), _cts.Token);
        }
    }

    private async Task OnNewTransaction(NewOnChainTransactionEvent obj)
    {
        if (obj.CryptoCode != "BTC") return;
        var identifier = obj.NewTransactionEvent.TrackedSource.ToString()!;
        var explorer = _explorerClientProvider.GetExplorerClient(obj.CryptoCode);
        var expandedTx = await explorer.GetTransactionAsync(obj.NewTransactionEvent.TrackedSource,
            obj.NewTransactionEvent.TransactionData.TransactionHash);
        await _hubContext.Clients
            .Group(identifier)
            .TransactionDetected(new TransactionDetectedRequest
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
        if (obj.CryptoCode != "BTC") return;
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
                    ExplorerClient.Track(trackedSource);
                }

                await AddToGroup(ts, contextConnectionId);
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

        await Handshake(contextConnectionId, new AppHandshake {Identifiers = result.Values.ToArray()});
        return result;
    }

private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    public async Task<bool> DeviceMasterSignal(string contextConnectionId, long deviceIdentifier, bool active)
    {
        var result = false;
        await _lock.WaitAsync();
        ConnectedInstance? connectedInstance = null;
        try
        {
        
        if (!Connections.TryGetValue(contextConnectionId, out connectedInstance))
        {
            _logger.LogWarning("DeviceMasterSignal called on non existing connection");
            result = false;
            return result;
        }
        else if(connectedInstance.DeviceIdentifier != null && connectedInstance.DeviceIdentifier != deviceIdentifier)
        {
            _logger.LogWarning("DeviceMasterSignal called with different device identifier");
            result = false;
            return result;
        }
        if(connectedInstance.DeviceIdentifier == null)
        {
            _logger.LogInformation("DeviceMasterSignal called with device identifier {deviceIdentifier}", deviceIdentifier);
            connectedInstance = connectedInstance with {DeviceIdentifier = deviceIdentifier};
            Connections[contextConnectionId] = connectedInstance;
        }
        
        if(connectedInstance.Master == active)
        {
            _logger.LogInformation("DeviceMasterSignal called with same active state");
            result = true;
            return result;
        }
        else if (active)
        {
            //check if there is any other master connection with the same user id
            if (Connections.Values.Any(c => c.UserId == connectedInstance.UserId && c.Master))
            {
                _logger.LogWarning("DeviceMasterSignal called with active state but there is already a master connection");
                result = false;
                return result;
            }
            else
            {
                _logger.LogInformation("DeviceMasterSignal called with active state");
                connectedInstance = connectedInstance with {Master = true};
                Connections[contextConnectionId] = connectedInstance;
                result = true;
                return result;
            }
        }
        else
        {
            _logger.LogInformation("DeviceMasterSignal called with inactive state");
            connectedInstance = connectedInstance with {Master = false};
            Connections[contextConnectionId] = connectedInstance;
          
            result = true;
            return result;
            
        }
        }
        finally
        {
            _lock.Release();
            if (result && connectedInstance is not null)
            {
                var connIds = Connections.Where(pair => pair.Value.UserId == connectedInstance.UserId)
                    .Select(pair => pair.Key)
                    .ToList();
                    
                await _hubContext.Clients.Clients(connIds).MasterUpdated(active? deviceIdentifier : null);
            }
        }
    }

    public async Task Disconnected(string contextConnectionId)
    {
        if (Connections.TryRemove(contextConnectionId, out var connectedInstance) && connectedInstance.Master)
        {
            MasterUserDisconnected?.Invoke(this, connectedInstance.UserId);
        }
    }
    
    public event EventHandler<string>? MasterUserDisconnected;

    public async Task Connected(string contextConnectionId, string userId)
    {
        Connections.TryAdd(contextConnectionId, new ConnectedInstance(userId, null, false,  new HashSet<string>()));
        
        if (_nodeInfo.Length > 0)
            await _hubContext.Clients.Client(contextConnectionId).NotifyServerNode(_nodeInfo);

        await _hubContext.Clients.Client(contextConnectionId)
            .NotifyNetwork(_networkProvider.BTC.NBitcoinNetwork.ToString());
        
        var groups = (await _storeRepository.GetStoresByUserId(userId)).Select(store => store.Id).ToArray().Concat(new[] {userId});
     
        foreach (var group in groups)
        {
            await AddToGroup(group, contextConnectionId);
        }
    }

    public async Task InvoiceUpdate(string identifier, LightningInvoice lightningInvoice)
    {
        _logger.LogInformation("Invoice update for {Identifier} {Amount} {PaymentHash}",
            identifier, lightningInvoice.Amount, lightningInvoice.PaymentHash);
        OnInvoiceUpdate?.Invoke(this, (identifier, lightningInvoice));
    }

    //what are we adding to groups?
    //user id
    //store id(s)
    //tracked sources

    private async Task AddToGroup(string group, params string[] connectionIds)
    {
        foreach (var connectionId in connectionIds)
        {
            await _hubContext.Groups.AddToGroupAsync(connectionId, group);
            if(Connections.TryGetValue(connectionId, out var connectedInstance))
            {
                connectedInstance.Groups.Add(group);
            }
        }
    }

    private async Task RemoveFromGroup(string group, params string[] connectionIds)
    {
        foreach (var connectionId in connectionIds)
        {
            await _hubContext.Groups.RemoveFromGroupAsync(connectionId, group);
            if(Connections.TryGetValue(connectionId, out var connectedInstance))
            {
                connectedInstance.Groups.Remove(group);
            }
        }
    }
}
