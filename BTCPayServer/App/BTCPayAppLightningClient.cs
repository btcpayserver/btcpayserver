using System;
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

namespace BTCPayServer.App;

public class BTCPayAppLightningClient : ILightningClient
{
    private readonly IHubContext<BTCPayAppHub, IBTCPayAppHubClient> _hubContext;
    private readonly BTCPayAppState _appState;
    private readonly string _key;

    public BTCPayAppLightningClient(IHubContext<BTCPayAppHub, IBTCPayAppHubClient> hubContext, BTCPayAppState appState,
        string key)
    {
        _hubContext = hubContext;
        _appState = appState;
        _key = key;
    }

    public override string ToString()
    {
        return $"type=app;user={_key}".ToLower();
    }

    public IBTCPayAppHubClient HubClient => _appState.Connections.FirstOrDefault(pair => pair.Value.Master && pair.Value.UserId == _key) is
    {
        Key: not null
    } connection
        ? _hubContext.Clients.Client(connection.Key)
        : throw new InvalidOperationException("Connection not found");


    public async Task<LightningInvoice> GetInvoice(string invoiceId,
        CancellationToken cancellation = new CancellationToken())
    {
        return await GetInvoice(uint256.Parse(invoiceId), cancellation);
    }

    public async Task<LightningInvoice> GetInvoice(uint256 paymentHash,
        CancellationToken cancellation = new CancellationToken())
    {
        return await HubClient.GetLightningInvoice(paymentHash);
    }

    public async Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = new CancellationToken())
    {
        return await ListInvoices(new ListInvoicesParams(), cancellation);
    }

    public async Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request,
        CancellationToken cancellation = new CancellationToken())
    {
        return (await HubClient.GetLightningInvoices(request)).ToArray();
    }

    public async Task<Lightning.LightningPayment> GetPayment(string paymentHash,
        CancellationToken cancellation = new CancellationToken())
    {
        return await HubClient.GetLightningPayment(uint256.Parse(paymentHash));
    }

    public async Task<Lightning.LightningPayment[]> ListPayments(
        CancellationToken cancellation = new CancellationToken())
    {
        return await ListPayments(new ListPaymentsParams(), cancellation);
    }

    public async Task<LightningPayment[]> ListPayments(ListPaymentsParams request,
        CancellationToken cancellation = new CancellationToken())
    {
        return (await HubClient.GetLightningPayments(request)).ToArray();
    }

    public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
        CancellationToken cancellation = new())
    {
        return await CreateInvoice(new CreateInvoiceParams(amount, description, expiry), cancellation);
    }

    public async Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest,
        CancellationToken cancellation = new CancellationToken())
    {
        return await HubClient.CreateInvoice(
            new CreateLightningInvoiceRequest(createInvoiceRequest.Amount, createInvoiceRequest.Description,
                createInvoiceRequest.Expiry)
            {
                DescriptionHashOnly = createInvoiceRequest.DescriptionHashOnly,
                PrivateRouteHints = createInvoiceRequest.PrivateRouteHints,
            });
    }

    public async Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = new CancellationToken())
    {
        return new Listener(_appState, _key);
    }

    public class Listener : ILightningInvoiceListener
    {
        private readonly BTCPayAppState _btcPayAppState;
        private readonly string _key;
        private readonly Channel<LightningInvoice> _channel = Channel.CreateUnbounded<LightningInvoice>();
        private readonly CancellationTokenSource _cts;

        public Listener(BTCPayAppState btcPayAppState, string key)
        {
            _btcPayAppState = btcPayAppState;
            btcPayAppState.MasterUserDisconnected += MasterUserDisconnected;
            _key = key;
            _cts = new CancellationTokenSource();
            _btcPayAppState.OnInvoiceUpdate += BtcPayAppStateOnOnInvoiceUpdate;
        }

        private void MasterUserDisconnected(object sender, string e)
        {
            if (e == _key)
                _channel.Writer.Complete();
        }

        private void BtcPayAppStateOnOnInvoiceUpdate(object sender, (string, LightningInvoice) e)
        {
            if (e.Item1.Equals(_key, StringComparison.InvariantCultureIgnoreCase))
                _channel.Writer.TryWrite(e.Item2);
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _btcPayAppState.OnInvoiceUpdate -= BtcPayAppStateOnOnInvoiceUpdate;
            _btcPayAppState.MasterUserDisconnected -= MasterUserDisconnected;
            _channel.Writer.TryComplete();
        }

        public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
        {
            return await _channel.Reader.ReadAsync(CancellationTokenSource
                .CreateLinkedTokenSource(cancellation, _cts.Token).Token);
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

    public async Task<PayResponse> Pay(PayInvoiceParams payParams,
        CancellationToken cancellation = new CancellationToken())
    {
        return await Pay(null, payParams, cancellation);
    }

    public async Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams,
        CancellationToken cancellation = new CancellationToken())
    {
        return await HubClient.PayInvoice(bolt11, payParams.Amount?.MilliSatoshi);
    }

    public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = new CancellationToken())
    {
        return await Pay(bolt11, new PayInvoiceParams(), cancellation);
    }

    public async Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest,
        CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public async Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo,
        CancellationToken cancellation = new CancellationToken())
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
