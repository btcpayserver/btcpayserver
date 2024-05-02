#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.RPC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers;

//TODO: this currently requires NBX to be enabled with RPCPROXY enabled, we need to fix the whitelisted rpc commands to remove this dependency
public static class Exts
{
    public static async Task<GetBlockchainInfoResponse> GetBlockchainInfoAsyncEx(this RPCClient client, CancellationToken cancellationToken = default)
    {
        var result = await client.SendCommandAsync("getblockchaininfo", cancellationToken).ConfigureAwait(false);
        return JsonConvert.DeserializeObject<GetBlockchainInfoResponse>(result.ResultString);
    }
    
    public static async Task<BlockHeaders> GetBlockHeadersAsync(this RPCClient rpc, IList<int> blockHeights, CancellationToken cancellationToken)
    {
        var batch = rpc.PrepareBatch();
        var hashes = blockHeights.Select(h => batch.GetBlockHashAsync(h)).ToArray();
        await batch.SendBatchAsync(cancellationToken);

        batch = rpc.PrepareBatch();
        var headers = hashes.Select(async h => await batch.GetBlockHeaderAsyncEx(await h, cancellationToken)).ToArray();
        await batch.SendBatchAsync(cancellationToken);

        return new BlockHeaders(headers.Select(h => h.GetAwaiter().GetResult()).Where(h => h is not null).ToList());
    }

    public static async Task<RPCBlockHeader> GetBlockHeaderAsyncEx(this RPCClient rpc, uint256 blk, CancellationToken cancellationToken)
    {
        var header = await rpc.SendCommandAsync(new NBitcoin.RPC.RPCRequest("getblockheader", new[] { blk.ToString() })
        {
            ThrowIfRPCError = false
        }, cancellationToken);
        if (header.Result is null || header.Error is not null)
            return null;
        var response = header.Result;
        var confs = response["confirmations"].Value<long>();
        if (confs == -1)
            return null;

        var prev = response["previousblockhash"]?.Value<string>();
        return new RPCBlockHeader(
            blk,
            prev is null ? null : new uint256(prev),
            response["height"].Value<int>(),
            NBitcoin.Utils.UnixTimeToDateTime(response["time"].Value<long>()),
            new uint256(response["merkleroot"]?.Value<string>()));
    }
    
}
