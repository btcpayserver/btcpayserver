#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.RPC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.App;

//TODO: this currently requires NBX to be enabled with RPCPROXY enabled, we need to fix the whitelisted rpc commands to remove this dependency
public static class Exts
{
    public static async Task<GetBlockchainInfoResponse?> GetBlockchainInfoAsyncEx(this RPCClient client, CancellationToken cancellationToken = default)
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

        var blockHeaders = headers
            .Select(h => h.GetAwaiter().GetResult())
            .Where(h => h != null)
            .OfType<RPCBlockHeader>()
            .ToList();
        return new BlockHeaders(blockHeaders);
    }

    private static async Task<RPCBlockHeader?> GetBlockHeaderAsyncEx(this RPCClient rpc, uint256 blk, CancellationToken cancellationToken)
    {
        var header = await rpc.SendCommandAsync(new RPCRequest("getblockheader", [blk.ToString()])
        {
            ThrowIfRPCError = false
        }, cancellationToken);
        if (header.Result is null || header.Error is not null) return null;

        var response = header.Result;
        var time = response["time"]?.Value<long>();
        var height = response["height"]?.Value<int>();
        var confs = response["confirmations"]?.Value<long>();
        if (time is null || height is null || confs is null or -1) return null;

        var prev = response["previousblockhash"]?.Value<string>();
        return new RPCBlockHeader(
            blk,
            prev is null ? null : new uint256(prev),
            height.Value,
            Utils.UnixTimeToDateTime(time.Value),
            new uint256(response["merkleroot"]?.Value<string>()));
    }
}
