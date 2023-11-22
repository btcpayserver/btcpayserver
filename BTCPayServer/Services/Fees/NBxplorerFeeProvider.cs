using System.Threading.Tasks;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;

namespace BTCPayServer.Services.Fees
{
    public class NBXplorerFeeProvider : BaseFeeProvider
    {
        private readonly ExplorerClient _explorerClient;

        public override async Task<FeeRate> GetFeeRate(int blockTarget = 20)
        {
                return (await _explorerClient.GetFeeRateAsync(blockTarget).ConfigureAwait(false)).FeeRate;
        }

        public NBXplorerFeeProvider(IFeeProvider fallback, ExplorerClient explorerClient) : base(fallback)
        {
            _explorerClient = explorerClient;
        }
    }
}
