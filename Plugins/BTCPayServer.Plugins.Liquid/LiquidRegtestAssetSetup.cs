#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using NBitcoin;

namespace BTCPayServer.Plugins.Liquid;

public class LiquidRegtestAssetSetup:IStartupTask
{
    private readonly ExplorerClientProvider _explorerClientProvider;
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

    public LiquidRegtestAssetSetup(ExplorerClientProvider explorerClientProvider, BTCPayNetworkProvider btcPayNetworkProvider)
    {
        _explorerClientProvider = explorerClientProvider;
        _btcPayNetworkProvider = btcPayNetworkProvider;
    }


    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if(_btcPayNetworkProvider.NetworkType != ChainName.Regtest)
            return;
        var lbtcrpc = _explorerClientProvider.GetExplorerClient("LBTC")?.RPCClient;
        if (lbtcrpc is null)
            return;
        var elements = _btcPayNetworkProvider.GetAll().OfType<ElementsBTCPayNetwork>();
            
        await lbtcrpc.SendCommandAsync("rescanblockchain");
        foreach (var element in elements)
        {
            try
            {
                if (element.AssetId is null)
                {
                    var issueAssetResult = await lbtcrpc.SendCommandAsync("issueasset", cancellationToken, 100000, 0);
                    element.AssetId = uint256.Parse(issueAssetResult.Result["asset"].ToString());
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
