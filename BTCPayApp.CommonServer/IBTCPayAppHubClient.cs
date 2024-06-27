using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using NBitcoin;
using LightningPayment = BTCPayApp.CommonServer.Models.LightningPayment;

namespace BTCPayApp.CommonServer;


public class TransactionDetectedRequest
{
    public string Identifier { get; set; }
    public string TxId { get; set; }
    public string[] SpentScripts { get; set; }
    public string[] ReceivedScripts { get; set; }
    public bool Confirmed { get; set; }
}


//methods available on the hub in the client
public interface IBTCPayAppHubClient
{
    Task NotifyServerEvent(IServerEvent ev);
    Task NotifyNetwork(string network);
    Task NotifyServerNode(string nodeInfo);
    Task TransactionDetected(TransactionDetectedRequest request);
    Task NewBlock(string block);

    Task<LightningPayment> CreateInvoice(CreateLightningInvoiceRequest createLightningInvoiceRequest);
    Task<LightningPayment?> GetLightningInvoice(string paymentHash);
    Task<LightningPayment?> GetLightningPayment(string paymentHash);
    Task<List<LightningPayment>> GetLightningPayments(ListPaymentsParams request);
    Task<List<LightningPayment>> GetLightningInvoices(ListInvoicesParams request);
    Task<PayResponse> PayInvoice(string bolt11, long? amountMilliSatoshi);
}

public interface IServerEvent
{
    public string Type { get; }
}

public interface IServerEvent<T> : IServerEvent
{
    public T? Event { get; }
}

public class ServerEvent(string type) : IServerEvent
{
    public string Type { get; } = type;
}
public class ServerEvent<T>(string type, T? evt = default) : ServerEvent(type), IServerEvent<T>
{
    public T? Event { get; } = evt;
}

public record TxResp(long Confirmations, long? Height, decimal BalanceChange, DateTimeOffset Timestamp, string TransactionId)
{
    public override string ToString()
    {
        return $"{{ Confirmations = {Confirmations}, Height = {Height}, BalanceChange = {BalanceChange}, Timestamp = {Timestamp}, TransactionId = {TransactionId} }}";
    }
}

//methods available on the hub in the server
public interface IBTCPayAppHubServer
{
    Task<bool> IdentifierActive(string group, bool active);
    
    Task<Dictionary<string,string>> Pair(PairRequest request);
    Task<AppHandshakeResponse> Handshake(AppHandshake request);
    Task<bool> BroadcastTransaction(string tx);
    Task<decimal> GetFeeRate(int blockTarget);
    Task<BestBlockResponse> GetBestBlock();
    Task<string> GetBlockHeader(string hash);
    
    Task<TxInfoResponse> FetchTxsAndTheirBlockHeads(string[] txIds);
    Task<string> DeriveScript(string identifier);
    Task TrackScripts(string identifier, string[] scripts);
    Task<string> UpdatePsbt(string[] identifiers, string psbt);
    Task<CoinResponse[]> GetUTXOs(string[] identifiers);
    Task<Dictionary<string, TxResp[]>> GetTransactions(string[] identifiers);


    Task SendPaymentUpdate(string identifier, LightningPayment lightningPayment);
}

public class CoinResponse
{
    public string Identifier{ get; set; }
    public bool Confirmed { get; set; }
    public string Script { get; set; }
    public string Outpoint { get; set; }
    public decimal Value { get; set; }
    public string Path { get; set; }
}

public class TxInfoResponse
{
    public Dictionary<string,TransactionResponse> Txs { get; set; }
    public Dictionary<string,string> Blocks { get; set; }
    public Dictionary<string,int> BlockHeghts { get; set; }
}

public class TransactionResponse
{
    public string? BlockHash { get; set; }
    public int? BlockHeight { get; set; }
    public string Transaction { get; set; }
    
}


public class BestBlockResponse
{
    public required string BlockHash { get; set; }
    public required int BlockHeight { get; set; }

    public string BlockHeader { get; set; }
}

public class AppHandshake
{
    public string[] Identifiers { get; set; }
}

public class AppHandshakeResponse
{
    //response about identifiers being tracked successfully
    public string[] IdentifiersAcknowledged { get; set; }
}


public class PairRequest
{
    public Dictionary<string, string?> Derivations { get; set; } = new();
}
