using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using NBitcoin;

namespace BTCPayApp.CommonServer;

//methods available on the hub in the client
public interface IBTCPayAppHubClient
{
    Task NotifyServerEvent(ServerEvent ev);
    Task NotifyNetwork(string network);
    Task NotifyServerNode(string nodeInfo);
    Task TransactionDetected(TransactionDetectedRequest request);
    Task NewBlock(string block);

    Task<LightningInvoice> CreateInvoice(CreateLightningInvoiceRequest createLightningInvoiceRequest);
    Task<LightningInvoice?> GetLightningInvoice(uint256 paymentHash);
    Task<LightningPayment?> GetLightningPayment(uint256 paymentHash);
    Task<List<LightningPayment>> GetLightningPayments(ListPaymentsParams request);
    Task<List<LightningInvoice>> GetLightningInvoices(ListInvoicesParams request);
    Task<PayResponse> PayInvoice(string bolt11, long? amountMilliSatoshi);
    Task MasterUpdated(long? deviceIdentifier);
    Task<LightningNodeInformation> GetLightningNodeInfo();
    Task<LightningNodeBalance> GetLightningBalance();
}

//methods available on the hub in the server
public interface IBTCPayAppHubServer
{
    Task<bool> DeviceMasterSignal(long deviceIdentifier, bool active);
    
    Task<Dictionary<string,string>> Pair(PairRequest request);
    Task<AppHandshakeResponse> Handshake(AppHandshake request);
    Task<bool> BroadcastTransaction(string tx);
    Task<decimal> GetFeeRate(int blockTarget);
    Task<BestBlockResponse> GetBestBlock();
    
    Task<TxInfoResponse> FetchTxsAndTheirBlockHeads(string identifier, string[] txIds, string[] outpoints);
    Task<string> DeriveScript(string identifier);
    Task TrackScripts(string identifier, string[] scripts);
    Task<string> UpdatePsbt(string[] identifiers, string psbt);
    Task<CoinResponse[]> GetUTXOs(string[] identifiers);
    Task<Dictionary<string, TxResp[]>> GetTransactions(string[] identifiers);

    Task SendInvoiceUpdate(LightningInvoice lightningInvoice);
    Task<long?> GetCurrentMaster();
}

public class ServerEvent(string type)
{
    public string Type { get; } = type;
    public string? StoreId { get; init; }
    public string? UserId { get; init; }
    public string? InvoiceId { get; init; }
    public string? Detail { get; set; }
}

public record TxResp(long Confirmations, long? Height, decimal BalanceChange, DateTimeOffset Timestamp, string TransactionId)
{
    public override string ToString()
    {
        return $"{{ Confirmations = {Confirmations}, Height = {Height}, BalanceChange = {BalanceChange}, Timestamp = {Timestamp}, TransactionId = {TransactionId} }}";
    }
}

public class TransactionDetectedRequest
{
    public string Identifier { get; set; }
    public string TxId { get; set; }
    public string[] SpentScripts { get; set; }
    public string[] ReceivedScripts { get; set; }
    public bool Confirmed { get; set; }
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
    public Dictionary<string,string> BlockHeaders { get; set; }
    public Dictionary<string,int> BlockHeghts { get; set; }
}

public class TransactionResponse
{
    public string? BlockHash { get; set; }
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
