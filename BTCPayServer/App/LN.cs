using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using BTCPayApp.CommonServer;
using BTCPayServer.Controllers;
using BTCPayServer.Lightning;
using Microsoft.AspNetCore.SignalR;
using NBitcoin;

namespace BTCPayServer.App;

public class BTCPayAppLightningConnectionStringHandler:ILightningConnectionStringHandler
{
    private readonly IHubContext<BTCPayAppHub, IBTCPayAppHubClient> _hubContext;
    private readonly BTCPayAppState _appState;
    private readonly DefaultHubLifetimeManager<BTCPayAppHub> _lifetimeManager;

    public BTCPayAppLightningConnectionStringHandler(IHubContext<BTCPayAppHub, IBTCPayAppHubClient> hubContext, BTCPayAppState appState)
    {
        _hubContext = hubContext;
        _appState = appState;
    }
    
    public ILightningClient Create(string connectionString, Network network, [UnscopedRef] out string error)
    {
        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (type != "app")
        {
            error = null;
            return null;
        }
        
        if (!kv.TryGetValue("group", out var key))
        {
            error = $"The key 'group' is mandatory for app connection strings";
            
            return null;
        }

        if (!_appState.GroupToConnectionId.TryGetValue(key, out var connectionId))
        {
            error = $"The group {key} is not connected";
            return null;
        }
        error = null;
        return new BTCPayAppLightningClient(_hubContext, _appState, key);
    }
    
    
}

public class BTCPayAppLightningClient:ILightningClient
{
    private readonly IHubContext<BTCPayAppHub, IBTCPayAppHubClient> _hubContext;
    private readonly BTCPayAppState _appState;
    private readonly string _key;

    public BTCPayAppLightningClient(IHubContext<BTCPayAppHub, IBTCPayAppHubClient> hubContext, BTCPayAppState appState, string key)
    {
        _hubContext = hubContext;
        _appState = appState;
        _key = key;
    }

    public override string ToString()
    {
        return $"type=app;group={_key}";
    }

    public IBTCPayAppHubClient HubClient => _appState.GroupToConnectionId.TryGetValue(_key, out var connId) ? _hubContext.Clients.Client(connId) : throw new InvalidOperationException("Connection not found");
    
    
    public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = new CancellationToken())
    {
        return await GetInvoice(uint256.Parse(invoiceId), cancellation);
    }

    public async Task<LightningInvoice> GetInvoice(uint256 paymentHash, CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = new CancellationToken())
    {
        return await ListInvoices(new ListInvoicesParams(), cancellation);
    }

    public async Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<LightningPayment[]> ListPayments(CancellationToken cancellation = new CancellationToken())
    {
        return await ListPayments(new ListPaymentsParams(), cancellation);
    }

    public async Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
        CancellationToken cancellation = new CancellationToken())
    {
        return await CreateInvoice(new CreateInvoiceParams(amount, description, expiry), cancellation);
    }

    public async Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest, CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = new CancellationToken())
    {
        return await Pay(bolt11, new PayInvoiceParams(), cancellation);
    }

    public async Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task CancelInvoice(string invoiceId, CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<LightningChannel[]> ListChannels(CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }
}
