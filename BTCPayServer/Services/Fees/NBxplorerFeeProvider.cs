#nullable enable
using System.Threading.Tasks;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;

namespace BTCPayServer.Services.Fees
{
    public class NBXplorerFeeProvider(ExplorerClient ExplorerClient) : IFeeProvider
    {
        public async Task<FeeRate> GetFeeRateAsync(int blockTarget = 20)
        {
            return (await ExplorerClient.GetFeeRateAsync(blockTarget).ConfigureAwait(false)).FeeRate;
        }
    }
}
