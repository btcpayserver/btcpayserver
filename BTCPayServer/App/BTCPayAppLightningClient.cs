using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Client.App;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Lightning;
using Microsoft.AspNetCore.SignalR;
using NBitcoin;

namespace BTCPayServer.App;

public class BTCPayAppLightningClient(
    IHubContext<BTCPayAppHub, IBTCPayAppHubClient> hubContext,
    BTCPayAppState appState,
    string key,
    string user)
    : ILightningClient
{
    public override string ToString()
    {
        return $"type=app;key={key};user={user}".ToLower();
    }

    private IBTCPayAppHubClient HubClient => appState.Connections.FirstOrDefault(pair => pair.Value.Master && pair.Value.UserId == user) is { Key: not null } connection
        ? hubContext.Clients.Client(connection.Key)
        : throw new InvalidOperationException("Connection not found");

    public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = new())
    {
        return await GetInvoice(uint256.Parse(invoiceId), cancellation);
    }

    public async Task<LightningInvoice> GetInvoice(uint256 paymentHash, CancellationToken cancellation = new())
    {
        return await HubClient.GetLightningInvoice(key, paymentHash);
    }

    public async Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = new())
    {
        return await ListInvoices(new ListInvoicesParams(), cancellation);
    }

    public async Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = new())
    {
        return (await HubClient.GetLightningInvoices(key, request)).ToArray();
    }

    public async Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = new())
    {
        return await HubClient.GetLightningPayment(key, uint256.Parse(paymentHash));
    }

    public async Task<LightningPayment[]> ListPayments(CancellationToken cancellation = new())
    {
        return await ListPayments(new ListPaymentsParams(), cancellation);
    }

    public async Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = new())
    {
        return (await HubClient.GetLightningPayments(key, request)).ToArray();
    }

    public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation = new())
    {
        return await CreateInvoice(new CreateInvoiceParams(amount, description, expiry), cancellation);
    }

    public async Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest, CancellationToken cancellation = new())
    {
        return await HubClient.CreateInvoice(key, 
            new CreateLightningInvoiceRequest(createInvoiceRequest.Amount, createInvoiceRequest.Description,
                createInvoiceRequest.Expiry)
            {
                DescriptionHashOnly = createInvoiceRequest.DescriptionHashOnly,
                PrivateRouteHints = createInvoiceRequest.PrivateRouteHints,
            });
    }

    public async Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = new())
    {
        await HubClient.StartListen(key);
        return new Listener(appState, user);
    }

    private class Listener : ILightningInvoiceListener
    {
        private readonly BTCPayAppState _btcPayAppState;
        private readonly string _userId;
        private readonly Channel<LightningInvoice> _channel = Channel.CreateUnbounded<LightningInvoice>();
        private readonly CancellationTokenSource _cts;

        public Listener(BTCPayAppState btcPayAppState, string userId)
        {
            _btcPayAppState = btcPayAppState;
            _userId = userId;
            btcPayAppState.MasterUserDisconnected += MasterUserDisconnected;
            _cts = new CancellationTokenSource();
            _btcPayAppState.OnInvoiceUpdate += BtcPayAppStateOnOnInvoiceUpdate;
        }

        private void MasterUserDisconnected(object sender, string e)
        {
            if (e == _userId)
                _channel.Writer.Complete();
        }

        private void BtcPayAppStateOnOnInvoiceUpdate(object sender, (string, LightningInvoice) e)
        {
            if (e.Item1.Equals(_userId, StringComparison.InvariantCultureIgnoreCase))
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

    public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = new())
    {
        return await  HubClient.GetLightningNodeInfo(key);
    }

    public async Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = new())
    {
       return await HubClient.GetLightningBalance(key);
    }

    public async Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation = new())
    {
        return await Pay(null, payParams, cancellation);
    }

    public async Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = new())
    {
        return await HubClient.PayInvoice(key, bolt11, payParams.Amount?.MilliSatoshi);
    }

    public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = new())
    {
        return await Pay(bolt11, new PayInvoiceParams(), cancellation);
    }

    public Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation = new())
    {
        throw new NotImplementedException();
    }

    public Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = new())
    {
        throw new NotImplementedException();
    }

    public Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation = new())
    {
        throw new NotImplementedException();
    }

    public async Task CancelInvoice(string invoiceId, CancellationToken cancellation = new())
    {
        await HubClient.CancelInvoice(key, uint256.Parse(invoiceId));
    }

    public Task<LightningChannel[]> ListChannels(CancellationToken cancellation = new())
    {
        throw new NotImplementedException();
    }
}
