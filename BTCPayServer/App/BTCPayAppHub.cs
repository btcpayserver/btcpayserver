#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayApp.CommonServer;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
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
    private readonly NBXplorerConnectionFactory _connectionFactory;
    private readonly NBXplorerDashboard _nbXplorerDashboard;
    private readonly BTCPayAppState _appState;
    private readonly ExplorerClientProvider _explorerClientProvider;
    private readonly IFeeProviderFactory _feeProviderFactory;
    private readonly ILogger<BTCPayAppHub> _logger;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ExplorerClient _explorerClient;
    private readonly BTCPayNetwork _network;

    public BTCPayAppHub(BTCPayNetworkProvider btcPayNetworkProvider,
        NBXplorerDashboard nbXplorerDashboard,
        BTCPayAppState appState,
        ExplorerClientProvider explorerClientProvider,
        IFeeProviderFactory feeProviderFactory,
        ILogger<BTCPayAppHub> logger,
        UserManager<ApplicationUser> userManager,
        NBXplorerConnectionFactory connectionFactory) 
    {
        _nbXplorerDashboard = nbXplorerDashboard;
        _appState = appState;
        _explorerClientProvider = explorerClientProvider;
        _feeProviderFactory = feeProviderFactory;
        _logger = logger;
        _userManager = userManager;
        _connectionFactory = connectionFactory;
        _network = btcPayNetworkProvider.BTC;
        _explorerClient =  _explorerClientProvider.GetExplorerClient(btcPayNetworkProvider.BTC);
        
        if (!_connectionFactory.Available || !_nbXplorerDashboard.IsFullySynched(_explorerClient.CryptoCode, out _))
        {
           Dispose();
           throw new InvalidOperationException("BTCPayAppHub is not available");
        }
    }

    public override async Task OnConnectedAsync()
    {
        if (!_connectionFactory.Available)
        {
            Context.Abort();
        }
        if (!_nbXplorerDashboard.IsFullySynched(_explorerClient.CryptoCode, out _))
        {
            Context.Abort();
        }
        var userId = _userManager.GetUserId(Context.User!)!;
        await _appState.Connected(Context.ConnectionId, userId);
        
        

        
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _appState.Disconnected(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<bool> BroadcastTransaction(string tx)
    {
       Transaction txObj = Transaction.Parse(tx, _network.NBitcoinNetwork);
       var result = await _explorerClient.BroadcastAsync(txObj);
       return result.Success;
    }

    public async Task<decimal> GetFeeRate(int blockTarget)
    {
        _logger.LogInformation($"Getting fee rate for {blockTarget}");
       
      var feeProvider =  _feeProviderFactory.CreateFeeProvider( _network);
      try
      {
          return (await feeProvider.GetFeeRateAsync(blockTarget)).SatoshiPerByte;
      }
      finally
      {
          _logger.LogInformation($"Getting fee rate for {blockTarget} done");
      }
    }

    public async Task<BestBlockResponse> GetBestBlock()
    {
        _logger.LogInformation($"Getting best block");
        var bcInfo = await _explorerClient.RPCClient.GetBlockchainInfoAsyncEx();
        var bh = await GetBlockHeader(bcInfo.BestBlockHash);
        _logger.LogInformation("Getting best block done");
        return new BestBlockResponse
        {
            BlockHash = bcInfo.BestBlockHash.ToString(),
            BlockHeight = bcInfo.Blocks,
            BlockHeader = Convert.ToHexString(bh.ToBytes())
        };
    }

    private async Task<BlockHeader> GetBlockHeader(uint256 hash)
    {
        var bh = await _explorerClient.RPCClient.GetBlockHeaderAsync(hash);
        return bh;
    }

    public async Task<TxInfoResponse> FetchTxsAndTheirBlockHeads(string identifier, string[] txIds , string[] outpointsRaw)
    {
        var cancellationToken = Context.ConnectionAborted;
        var outpoints = outpointsRaw.Select(OutPoint.Parse).ToArray();
        var ts = TrackedSource.Parse(identifier,_explorerClient.Network);
      
        await using var conn = await _connectionFactory.OpenConnection();
        var txs = await conn.QueryAsync("""
                                        SELECT txs.blk_height, txs.blk_id, txs.raw FROM txs 
                                        WHERE code = @code AND 
                                              (tx_id = ANY(@tx_ids) OR 
                                               tx_id IN (SELECT DISTINCT(ins.tx_id) 
                                                         FROM ins
                                                         INNER JOIN wallets_scripts 
                                                         ON ins.script = wallets_scripts.script
                                                         WHERE ins.code = @code 
                                                         AND wallets_scripts.wallet_id = @wallet_id 
                                                         AND (ins.spent_tx_id || '-' || ins.spent_idx) = ANY(@outpoints))
                                              )
                                        """,
            new
            {
                code = _explorerClient.CryptoCode,
                tx_ids = txIds,
                outpoints = outpoints.Select(point => point.ToString()).ToArray(),
                wallet_id = identifier.Replace("GROUP:", "G:")
            });
        var data = txs.Select(row => new
        {
            Height = (long?)row.blk_height,
            BlockHash = (string?)row.blk_id,
            Transaction = Transaction.Load((byte[])row.raw, _network.NBitcoinNetwork)
        }).ToArray();
        
 
        var blocksToFetch = data.Where(row => row.BlockHash is not null).Select(row => (uint256.Parse(row.BlockHash!), row.Height!)).DistinctBy(x => x.Item1).ToArray();
        
        
        var batch = _explorerClient.RPCClient.PrepareBatch();
        var headersTask = blocksToFetch.ToDictionary(result => result.Item1, result =>
                (batch.GetBlockHeaderAsync(result.Item1, cancellationToken), result.Item2! ));
        await batch.SendBatchAsync(cancellationToken);
        await Task.WhenAll(headersTask.Values.Select(kv => (Task) kv.Item1));
        return new TxInfoResponse
        {
            Txs = data.ToDictionary(tx => tx.Transaction.GetHash().ToString(), tx => new TransactionResponse
            {
                BlockHash = tx.BlockHash?.ToString(),
                Transaction = tx.Transaction.ToHex()
            }),
            BlockHeaders = headersTask.ToDictionary(kv => kv.Key.ToString(), kv => Convert.ToHexString(kv.Value.Item1.Result.ToBytes()).ToLower()),
            BlockHeghts = headersTask.ToDictionary(kv => kv.Key.ToString(), kv => (int) kv.Value.Item2!)
        };
    }

    public async Task<string> DeriveScript(string identifier)
    {
        var cancellationToken = Context.ConnectionAborted;
        var ts = TrackedSource.Parse(identifier,_explorerClient.Network ) as DerivationSchemeTrackedSource;
        var kpi = await _explorerClient.GetUnusedAsync(ts.DerivationStrategy, DerivationFeature.Deposit, 0, true, cancellationToken);
        return kpi.ScriptPubKey.ToHex();
    }

    public async Task TrackScripts(string identifier, string[] scripts)
    {
        _logger.LogInformation($"Tracking {scripts.Length} scripts for {identifier}");
        
        var ts = TrackedSource.Parse(identifier,_explorerClient.Network ) as GroupTrackedSource;
        var s = scripts.Select(Script.FromHex).Select(script => script.GetDestinationAddress(_explorerClient.Network.NBitcoinNetwork)).Select(address => address.ToString()).ToArray();
        await _explorerClient.AddGroupAddressAsync(_explorerClient.CryptoCode,ts.GroupId, s);
        
        _logger.LogInformation($"Tracking {scripts.Length} scripts for {identifier} done ");
    }

    public async Task<string> UpdatePsbt(string[] identifiers, string psbt)
    {
        var resultPsbt = PSBT.Parse(psbt, _explorerClient.Network.NBitcoinNetwork);
        foreach (string identifier in identifiers)
        {
            var ts = TrackedSource.Parse(identifier,_explorerClient.Network);
            if (ts is not DerivationSchemeTrackedSource derivationSchemeTrackedSource)
                continue;
            var res = await _explorerClient.UpdatePSBTAsync(new UpdatePSBTRequest()
            {
                PSBT = resultPsbt, DerivationScheme = derivationSchemeTrackedSource.DerivationStrategy,
            });   
            resultPsbt = resultPsbt.Combine(res.PSBT);
        }
        return resultPsbt.ToHex();
    }

    public async Task<CoinResponse[]> GetUTXOs(string[] identifiers)
    {
        var result = new List<CoinResponse>();
        foreach (string identifier in identifiers)
        {
            var ts = TrackedSource.Parse(identifier,_explorerClient.Network);
            if (ts is null)
            {
                continue;
            }
            var utxos = await _explorerClient.GetUTXOsAsync(ts);
            result.AddRange(utxos.GetUnspentUTXOs(0).Select(utxo => new CoinResponse()
            {
                Identifier = identifier,
                Confirmed = utxo.Confirmations >0,
                Script = utxo.ScriptPubKey.ToHex(),
                Outpoint = utxo.Outpoint.ToString(),
                Value = utxo.Value.GetValue(_network),
                Path = utxo.KeyPath?.ToString()
            }));
        }
        return result.ToArray();
    }

    public async Task<Dictionary<string, TxResp[]>> GetTransactions(string[] identifiers)
    {
        var result = new Dictionary<string, TxResp[]>();
        foreach (string identifier in identifiers)
        {
            var ts = TrackedSource.Parse(identifier,_explorerClient.Network);
            if (ts is null)
            {
                continue;
            }
            var txs = await _explorerClient.GetTransactionsAsync(ts);
            
            var items = txs.ConfirmedTransactions.Transactions
                .Concat(txs.UnconfirmedTransactions.Transactions)
                .Concat(txs.ImmatureTransactions.Transactions)
                .Concat(txs.ReplacedTransactions.Transactions)
                .Select(tx => new TxResp(tx.Confirmations, tx.Height, tx.BalanceChange.GetValue(_network), tx.Timestamp, tx.TransactionId.ToString())).OrderByDescending(arg => arg.Timestamp);
            result.Add(identifier,items.ToArray());
        }

        return result;
    }
    public async Task SendInvoiceUpdate( LightningInvoice lightningInvoice)
    {
        
        await _appState. InvoiceUpdate(Context.ConnectionId, lightningInvoice);
    }

    public async Task<long?> GetCurrentMaster()
    {
        return await _appState.GetCurrentMaster(Context.ConnectionId);
    }

    public async Task<bool> DeviceMasterSignal(long deviceIdentifier, bool active)
    {
        return await _appState.DeviceMasterSignal(Context.ConnectionId,deviceIdentifier,active);
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


