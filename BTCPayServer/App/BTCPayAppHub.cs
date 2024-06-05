#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayApp.CommonServer;
using BTCPayApp.CommonServer.Models;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.HostedServices;
using BTCPayServer.Services;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;

namespace BTCPayServer.Controllers;


public class GetBlockchainInfoResponse
{
    [JsonProperty("headers")]
    public int Headers
    {
        get; set;
    }
    [JsonProperty("blocks")]
    public int Blocks
    {
        get; set;
    }
    [JsonProperty("verificationprogress")]
    public double VerificationProgress
    {
        get; set;
    }

    [JsonProperty("mediantime")]
    public long? MedianTime
    {
        get; set;
    }

    [JsonProperty("initialblockdownload")]
    public bool? InitialBlockDownload
    {
        get; set;
    }
    [JsonProperty("bestblockhash")]
    [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
    public uint256 BestBlockHash { get; set; }
}


public record RPCBlockHeader(uint256 Hash, uint256? Previous, int Height, DateTimeOffset Time, uint256 MerkleRoot)
{
    public SlimChainedBlock ToSlimChainedBlock() => new(Hash, Previous, Height);
}
public class BlockHeaders : IEnumerable<RPCBlockHeader>
{
    public readonly Dictionary<uint256, RPCBlockHeader> ByHashes;
    public readonly Dictionary<int, RPCBlockHeader> ByHeight;
    public BlockHeaders(IList<RPCBlockHeader> headers)
    {
        ByHashes = new Dictionary<uint256, RPCBlockHeader>(headers.Count);
        ByHeight = new Dictionary<int, RPCBlockHeader>(headers.Count);
        foreach (var header in headers)
        {
            ByHashes.TryAdd(header.Hash, header);
            ByHeight.TryAdd(header.Height, header);
        }
    }
    public IEnumerator<RPCBlockHeader> GetEnumerator()
    {
        return ByHeight.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

[Authorize(AuthenticationSchemes = AuthenticationSchemes.GreenfieldBearer)]
public class BTCPayAppHub : Hub<IBTCPayAppHubClient>, IBTCPayAppHubServer
{
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly NBXplorerDashboard _nbXplorerDashboard;
    private readonly BTCPayAppState _appState;
    private readonly ExplorerClientProvider _explorerClientProvider;
    private readonly IFeeProviderFactory _feeProviderFactory;
    private readonly ILogger<BTCPayAppHub> _logger;

    public BTCPayAppHub(BTCPayNetworkProvider btcPayNetworkProvider,
        NBXplorerDashboard nbXplorerDashboard,
        BTCPayAppState appState,
        ExplorerClientProvider explorerClientProvider,
        IFeeProviderFactory feeProviderFactory,
        ILogger<BTCPayAppHub> logger) 
    {
        _btcPayNetworkProvider = btcPayNetworkProvider;
        _nbXplorerDashboard = nbXplorerDashboard;
        _appState = appState;
        _explorerClientProvider = explorerClientProvider;
        _feeProviderFactory = feeProviderFactory;
        _logger = logger;
    }


    public override async Task OnConnectedAsync()
    {
        
        await _appState.Connected(Context.ConnectionId);
        
        //TODO: this needs to happen BEFORE connection is established
        if (!_nbXplorerDashboard.IsFullySynched(_btcPayNetworkProvider.BTC.CryptoCode, out _))
        {
            Context.Abort();
            return;
        }
        await Clients.Client(Context.ConnectionId).NotifyNetwork(_btcPayNetworkProvider.BTC.NBitcoinNetwork.ToString());
        

    }


    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _appState.Disconnected(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }


    public async Task<bool> BroadcastTransaction(string tx)
    {
       var explorerClient =  _explorerClientProvider.GetExplorerClient( _btcPayNetworkProvider.BTC);
       Transaction txObj = Transaction.Parse(tx, explorerClient.Network.NBitcoinNetwork);
       var result = await explorerClient.BroadcastAsync(txObj);
       return result.Success;
    }

    public async Task<decimal> GetFeeRate(int blockTarget)
    {
       
      var feeProvider =  _feeProviderFactory.CreateFeeProvider( _btcPayNetworkProvider.BTC);
      return (await feeProvider.GetFeeRateAsync(blockTarget)).SatoshiPerByte;
    }

    public async Task<BestBlockResponse> GetBestBlock()
    {
        
        var explorerClient =  _explorerClientProvider.GetExplorerClient( _btcPayNetworkProvider.BTC);
        var bcInfo = await explorerClient.RPCClient.GetBlockchainInfoAsyncEx();
        var bh = await GetBlockHeader(bcInfo.BestBlockHash.ToString());
        
        return new BestBlockResponse()
        {
            BlockHash = bcInfo.BestBlockHash.ToString(),
            BlockHeight = bcInfo.Blocks,
            BlockHeader = bh
        };
    }

    public async Task<string> GetBlockHeader(string hash)
    {
        
        var explorerClient =  _explorerClientProvider.GetExplorerClient( _btcPayNetworkProvider.BTC);
        var bh = await explorerClient.RPCClient.GetBlockHeaderAsync(uint256.Parse(hash));
        return Convert.ToHexString(bh.ToBytes());
    }

    public async Task<TxInfoResponse> FetchTxsAndTheirBlockHeads(string[] txIds)
    { 
        
        var cancellationToken = Context.ConnectionAborted;
        var explorerClient =  _explorerClientProvider.GetExplorerClient( _btcPayNetworkProvider.BTC);
        var uints = txIds.Select(uint256.Parse).ToArray();
        var txsFetch = await Task.WhenAll(uints.Select(
            uint256 =>
                explorerClient.GetTransactionAsync(uint256, cancellationToken)));

        var batch = explorerClient.RPCClient.PrepareBatch();
        var headersTask = txsFetch.Where(result => result.BlockId is not null && result.BlockId != uint256.Zero)
            .Distinct().ToDictionary(result => result.BlockId, result =>
                batch.GetBlockHeaderAsync(result.BlockId, cancellationToken));
        await batch.SendBatchAsync(cancellationToken);

        
        
        var headerToHeight = (await Task.WhenAll(headersTask.Values)).ToDictionary(header => header.GetHash(),
            header => txsFetch.First(result => result.BlockId == header.GetHash()).Height!);
        
        return new TxInfoResponse()
        {
            Txs = txsFetch.ToDictionary(tx => tx.TransactionHash.ToString(), tx => new TransactionResponse()
            {
                BlockHash = tx.BlockId?.ToString(),
                BlockHeight = (int?) tx.Height,
                Transaction = tx.Transaction.ToHex()
            }),
            Blocks = headersTask.ToDictionary(kv => kv.Key.ToString(), kv => Convert.ToHexString(kv.Value.Result.ToBytes())),
            BlockHeghts = headerToHeight.ToDictionary(kv => kv.Key.ToString(), kv =>(int) kv.Value!)
        };
    }
    public async Task<string> DeriveScript(string identifier)
    {
        var cancellationToken = Context.ConnectionAborted;
        var explorerClient =  _explorerClientProvider.GetExplorerClient( _btcPayNetworkProvider.BTC);
        var ts = TrackedSource.Parse(identifier,explorerClient.Network ) as DerivationSchemeTrackedSource;
        var kpi = await explorerClient.GetUnusedAsync(ts.DerivationStrategy, DerivationFeature.Deposit, 0, true, cancellationToken);
        return kpi.ScriptPubKey.ToHex();
    }

    public async Task TrackScripts(string identifier, string[] scripts)
    {
        _logger.LogInformation($"Tracking {scripts.Length} scripts for {identifier}");
        var explorerClient =  _explorerClientProvider.GetExplorerClient( _btcPayNetworkProvider.BTC);
        
        var ts = TrackedSource.Parse(identifier,explorerClient.Network ) as GroupTrackedSource;
        var s = scripts.Select(Script.FromHex).Select(script => script.GetDestinationAddress(explorerClient.Network.NBitcoinNetwork)).Select(address => address.ToString()).ToArray();
        await explorerClient.AddGroupAddressAsync(explorerClient.CryptoCode,ts.GroupId, s);
        
        _logger.LogInformation($"Tracking {scripts.Length} scripts for {identifier} done ");
    }

    public async Task<string> UpdatePsbt(string[] identifiers, string psbt)
    {
        var explorerClient =  _explorerClientProvider.GetExplorerClient( _btcPayNetworkProvider.BTC);
var resultPsbt = PSBT.Parse(psbt, explorerClient.Network.NBitcoinNetwork);
        foreach (string identifier in identifiers)
        {
            var ts = TrackedSource.Parse(identifier,explorerClient.Network);
            if (ts is not DerivationSchemeTrackedSource derivationSchemeTrackedSource)
                continue;
            var res = await explorerClient.UpdatePSBTAsync(new UpdatePSBTRequest()
            {
                PSBT = resultPsbt, DerivationScheme = derivationSchemeTrackedSource.DerivationStrategy,
            });   
            resultPsbt = resultPsbt.Combine(res.PSBT);
        }
        return resultPsbt.ToHex();
    }

    public async Task<CoinResponse[]> GetUTXOs(string[] identifiers)
    {
        var explorerClient =  _explorerClientProvider.GetExplorerClient( _btcPayNetworkProvider.BTC);
        var result = new List<CoinResponse>();
        foreach (string identifier in identifiers)
        {
            var ts = TrackedSource.Parse(identifier,explorerClient.Network);
            if (ts is null)
            {
                continue;
            }
            var utxos = await explorerClient.GetUTXOsAsync(ts);
            result.AddRange(utxos.GetUnspentUTXOs(0).Select(utxo => new CoinResponse()
            {
                Identifier = identifier,
                Confirmed = utxo.Confirmations >0,
                Script = utxo.ScriptPubKey.ToHex(),
                Outpoint = utxo.Outpoint.ToString(),
                Value = utxo.Value.GetValue(_btcPayNetworkProvider.BTC),
                Path = utxo.KeyPath?.ToString()
            }));
        }
        return result.ToArray();
    }

    public async Task<Dictionary<string, TxResp[]>> GetTransactions(string[] identifiers)
    {
        var explorerClient =  _explorerClientProvider.GetExplorerClient( _btcPayNetworkProvider.BTC);
        var result = new Dictionary<string, TxResp[]>();
        foreach (string identifier in identifiers)
        {
            var ts = TrackedSource.Parse(identifier,explorerClient.Network);
            if (ts is null)
            {
                continue;
            }
            var txs = await explorerClient.GetTransactionsAsync(ts);
            
            var items = txs.ConfirmedTransactions.Transactions
                .Concat(txs.UnconfirmedTransactions.Transactions)
                .Concat(txs.ImmatureTransactions.Transactions)
                .Concat(txs.ReplacedTransactions.Transactions)
                .Select(tx => new TxResp(tx.Confirmations, tx.Height, tx.BalanceChange.GetValue(_btcPayNetworkProvider.BTC), tx.Timestamp, tx.TransactionId.ToString())).OrderByDescending(arg => arg.Timestamp);
            result.Add(identifier,items.ToArray());
        }

        return result;
    }

    public async Task SendPaymentUpdate(string identifier, LightningPayment lightningPayment)
    {
        await _appState.PaymentUpdate(identifier, lightningPayment);
    }


    public async Task<bool> IdentifierActive(string group, bool active)
    {
        
        return await _appState.IdentifierActive(group, Context.ConnectionId, active);
    }


    public async Task<Dictionary<string, string>> Pair(PairRequest request)
    {
        return await _appState.Pair(Context.ConnectionId, request);
        
        
    }
    

    public async Task<AppHandshakeResponse> Handshake(AppHandshake request)
    {
        
        return await _appState.Handshake(Context.ConnectionId, request);
    }
}


