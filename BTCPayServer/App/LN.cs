using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayApp.CommonServer;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Lightning;
using Microsoft.AspNetCore.SignalR;
using NBitcoin;
using LightningPayment = BTCPayApp.CommonServer.Models.LightningPayment;

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
        return new BTCPayAppLightningClient(_hubContext, _appState, key, network );
    }
    
    
}

public class BTCPayAppLightningClient:ILightningClient
{
    private readonly IHubContext<BTCPayAppHub, IBTCPayAppHubClient> _hubContext;
    private readonly BTCPayAppState _appState;
    private readonly string _key;
    private readonly Network _network;

    public BTCPayAppLightningClient(IHubContext<BTCPayAppHub, IBTCPayAppHubClient> hubContext, BTCPayAppState appState, string key, Network network)
    {
        _hubContext = hubContext;
        _appState = appState;
        _key = key;
        _network = network;
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
        var lp = await HubClient.GetLightningInvoice(paymentHash.ToString());
        return ToLightningInvoice(lp, _network);
    }

    public async Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = new CancellationToken())
    {
        return await ListInvoices(new ListInvoicesParams(), cancellation);
    }

    public async Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = new CancellationToken())
    {
        var invs = await HubClient.GetLightningInvoices(request);
        return invs.Select(i => ToLightningInvoice(i, _network)).ToArray();
    }

    public async Task<Lightning.LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = new CancellationToken())
    {

        return ToLightningPayment(await HubClient.GetLightningPayment(paymentHash));
    }

    private static Lightning.LightningPayment ToLightningPayment(LightningPayment lightningPayment)
    {
        return new Lightning.LightningPayment()
        {
            Id = lightningPayment.PaymentHash,
            Amount = LightMoney.MilliSatoshis(lightningPayment.Value),
            PaymentHash = lightningPayment.PaymentHash,
            Preimage = lightningPayment.Preimage,
            BOLT11 = lightningPayment.PaymentRequests.FirstOrDefault(),
            Status = lightningPayment.Status
        };
    }

    public async Task<Lightning.LightningPayment[]> ListPayments(CancellationToken cancellation = new CancellationToken())
    {
        return await ListPayments(new ListPaymentsParams(), cancellation);
    }

    public async Task<Lightning.LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = new CancellationToken())
    {
        var invs = await HubClient.GetLightningPayments(request);
        return invs.Select(ToLightningPayment).ToArray();
    }

    public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
        CancellationToken cancellation = new CancellationToken())
    {
        return await CreateInvoice(new CreateInvoiceParams(amount, description, expiry), cancellation);
    }

    public async Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest, CancellationToken cancellation = new CancellationToken())
    {
        var lp = await HubClient.CreateInvoice(new CreateLightningInvoiceRequest(createInvoiceRequest.Amount, createInvoiceRequest.Description, createInvoiceRequest.Expiry)
        {
            DescriptionHashOnly = createInvoiceRequest.DescriptionHashOnly,
            PrivateRouteHints = createInvoiceRequest.PrivateRouteHints,
            
        });
        return ToLightningInvoice(lp, _network);
    }

    private static LightningInvoice ToLightningInvoice(LightningPayment lightningPayment, Network _network)
    {
        var paymenRequest = BOLT11PaymentRequest.Parse(lightningPayment.PaymentRequests.First(), _network);
        return new LightningInvoice()
        {
            Id = lightningPayment.PaymentHash,
            Amount = LightMoney.MilliSatoshis(lightningPayment.Value),
            PaymentHash = lightningPayment.PaymentHash,
            Preimage = lightningPayment.Preimage,
            BOLT11 = lightningPayment.PaymentRequests.FirstOrDefault(),
            Status = lightningPayment.Status == LightningPaymentStatus.Complete? LightningInvoiceStatus.Paid: paymenRequest.ExpiryDate < DateTimeOffset.UtcNow? LightningInvoiceStatus.Expired: LightningInvoiceStatus.Unpaid
        };
    }
    
    public async Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = new CancellationToken())
    {
        
        return new Listener(_appState, _network, _key);
    }

    public class Listener:ILightningInvoiceListener
    {
        private readonly BTCPayAppState _btcPayAppState;
        private readonly Network _network;
        private readonly string _key;
        private readonly Channel<LightningPayment> _channel = Channel.CreateUnbounded<LightningPayment>();
        private readonly CancellationTokenSource _cts;

        public Listener(BTCPayAppState btcPayAppState, Network network, string key)
        {
            _btcPayAppState = btcPayAppState;
            btcPayAppState.GroupRemoved += BtcPayAppStateOnGroupRemoved;
            _network = network;
            _key = key;
            _cts = new CancellationTokenSource();
            _btcPayAppState.OnPaymentUpdate += BtcPayAppStateOnOnPaymentUpdate;
        }

        private void BtcPayAppStateOnGroupRemoved(object sender, string e)
        {
            if(e == _key)
                _channel.Writer.Complete();
        }

        private void BtcPayAppStateOnOnPaymentUpdate(object sender, (string, LightningPayment) e)
        {
            if(e.Item1 != _key)
                return;
            _channel.Writer.TryWrite(e.Item2);
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _btcPayAppState.OnPaymentUpdate -= BtcPayAppStateOnOnPaymentUpdate;
            _btcPayAppState.GroupRemoved -= BtcPayAppStateOnGroupRemoved;
            _channel.Writer.TryComplete();
        }

        public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
        {
            return ToLightningInvoice(await _channel.Reader.ReadAsync( CancellationTokenSource.CreateLinkedTokenSource(cancellation, _cts.Token).Token), _network);
        }
    }

    public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = new CancellationToken())
    {
        throw new NotSupportedException();
    }

    public async Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation = new CancellationToken())
    {
        return await Pay(null, payParams, cancellation);
    }

    public async Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = new CancellationToken())
    {
        return await HubClient.PayInvoice(bolt11, payParams.Amount?.MilliSatoshi);
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
